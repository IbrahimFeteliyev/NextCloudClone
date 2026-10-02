using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Atlas.Api.Auth;
using Atlas.Api.Controllers;
using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Atlas.Tests;
public sealed class FileContentTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly User owner = new() { Email = "owner@test.local", Name = "Owner", RoleId = 1 };
    private readonly User reader = new() { Email = "reader@test.local", Name = "Reader", RoleId = 1 };
    private readonly Folder folder; private readonly Document file;
    private readonly IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "test-key-for-preview-only-32-bytes-long", ["Jwt:Issuer"] = "tests" }).Build();
    public FileContentTests()
    {
        connection.Open(); db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        db.Roles.Add(new() { Id = 1, Name = "Employee" }); db.Users.AddRange(owner, reader);
        folder = new() { Name = "Shared", OwnerId = owner.Id, IsRoot = true };
        file = new() { Name = "demo.mp4", OriginalName = "demo.mp4", ObjectKey = "private/test", OwnerId = owner.Id, ParentFolderId = folder.Id, Size = 10 };
        db.Folders.Add(folder); db.Files.Add(file); db.SaveChanges();
    }
    private DefaultHttpContext Context(User? actor)
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        if (actor != null) context.User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString())], "test"));
        return context;
    }
    private FileContentService Service(DefaultHttpContext context) => new(db, new(new HttpContextAccessor { HttpContext = context }), new(db), config);
    private async Task Grant() { db.FolderPermissions.Add(new() { FolderId = folder.Id, UserId = reader.Id, Access = Access.Read }); await db.SaveChangesAsync(); }
    private FileContentController Controller(DefaultHttpContext context, FakeStorage storage) => new(Service(context), new(new HttpContextAccessor { HttpContext = context }), storage, NullLogger<FileContentController>.Instance) { ControllerContext = new ControllerContext { HttpContext = context } };
    [Theory]
    [InlineData(null, 0, 10, false)] [InlineData("bytes=2-4", 2, 3, true)] [InlineData("bytes=8-", 8, 2, true)]
    [InlineData("bytes=-3", 7, 3, true)] [InlineData("bytes=-99", 0, 10, true)] [InlineData("bytes=0-99", 0, 10, true)]
    public void Ranges_are_bounded(string? header, long offset, long length, bool partial) => Assert.Equal(new ContentRange(offset, length, partial), ContentRange.Parse(header, 10));
    [Theory] [InlineData("bytes=10-")] [InlineData("bytes=6-2")] [InlineData("bytes=-0")] [InlineData("bytes=0-1,4-5")] [InlineData("items=0-1")]
    public void Invalid_ranges_return_416(string header) => Assert.Equal(416, Assert.Throws<ApiException>(() => ContentRange.Parse(header, 10)).Status);
    [Fact] public async Task Range_streams_only_requested_bytes_and_inline_headers()
    {
        await Grant(); var context = Context(reader); context.Request.Headers.Range = "bytes=2-4"; var storage = new FakeStorage();
        await Controller(context, storage).Content(file.Id);
        Assert.Equal(206, context.Response.StatusCode); Assert.Equal("bytes 2-4/10", context.Response.Headers.ContentRange); Assert.Equal("inline", context.Response.Headers.ContentDisposition);
        Assert.Equal("video/mp4", context.Response.ContentType); Assert.Equal(3, context.Response.ContentLength);
        Assert.Equal(new byte[] { 2, 3, 4 }, ((MemoryStream)context.Response.Body).ToArray()); Assert.Equal((2L, 3L), storage.Range);
    }
    [Fact] public async Task Head_and_empty_files_do_not_read_storage()
    {
        var context = Context(owner); context.Request.Method = "HEAD"; var storage = new FakeStorage(); await Controller(context, storage).Content(file.Id); Assert.Null(storage.Range);
        file.Size = 0; await db.SaveChangesAsync(); context.Request.Method = "GET"; await Controller(context, storage).Content(file.Id); Assert.Null(storage.Range); Assert.Equal(0, context.Response.ContentLength);
    }
    [Fact] public async Task Authentication_missing_permission_and_missing_file_are_rejected()
    {
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Controller(Context(null), new()).Content(file.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(Context(reader)).CreateTicket(file.Id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => Service(Context(owner)).CreateTicket(Guid.NewGuid()))).Status);
    }
    [Fact] public async Task Ticket_is_file_scoped_and_tamper_resistant()
    {
        await Grant(); var service = Service(Context(reader)); var ticket = await service.CreateTicket(file.Id); var token = ticket.ContentPath.Split("preview_token=")[1];
        Assert.Equal(reader.Id, service.ValidateTicket(token, file.Id).Actor);
        Assert.Equal(401, Assert.Throws<ApiException>(() => service.ValidateTicket(token, Guid.NewGuid())).Status);
        Assert.Equal(401, Assert.Throws<ApiException>(() => service.ValidateTicket(token + "tampered", file.Id)).Status);
        Assert.InRange(ticket.ExpiresAt - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }
    [Theory] [InlineData("atlas-file-preview", true)] [InlineData("atlas-api", false)]
    public void Expired_tokens_and_session_tokens_are_not_media_tickets(string audience, bool expired)
    {
        var jwt = new JwtSecurityToken(issuer: "tests", audience: audience,
            claims: [new("sub", owner.Id.ToString()), new("file", file.Id.ToString()), new("version", "1"), new("purpose", "content")],
            notBefore: DateTime.UtcNow.AddHours(-1), expires: expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        Assert.Equal(401, Assert.Throws<ApiException>(() => Service(Context(null)).ValidateTicket(new JwtSecurityTokenHandler().WriteToken(jwt), file.Id)).Status);
    }
    [Fact] public async Task Revocation_and_direct_deny_apply_to_previously_issued_tickets()
    {
        await Grant(); var ticket = await Service(Context(reader)).CreateTicket(file.Id); var token = ticket.ContentPath.Split("preview_token=")[1];
        db.FilePermissions.Add(new() { FileId = file.Id, UserId = reader.Id, Access = Access.None }); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Controller(Context(null), new()).Content(file.Id, token))).Status);
    }
    [Fact] public async Task Changed_version_cannot_mix_bytes_in_existing_media_session()
    {
        var ticket = await Service(Context(owner)).CreateTicket(file.Id); file.Version++; await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Controller(Context(null), new()).Content(file.Id, ticket.ContentPath.Split("preview_token=")[1]))).Status);
    }
    [Fact] public async Task IfRange_mismatch_returns_full_current_representation()
    {
        var context = Context(owner); context.Request.Headers.Range = "bytes=2-4"; context.Request.Headers.IfRange = "\"old-version\""; var storage = new FakeStorage();
        await Controller(context, storage).Content(file.Id); Assert.Equal(200, context.Response.StatusCode); Assert.Equal((0L, 10L), storage.Range);
    }
    [Fact] public async Task Unavailable_storage_returns_clear_502()
    { Assert.Equal(502, (await Assert.ThrowsAsync<ApiException>(() => Controller(Context(owner), new() { Fail = true }).Content(file.Id))).Status); }
    [Theory] [InlineData("application/pdf", "misleading.txt", "application/pdf")] [InlineData("text/plain", "notes.md", "text/markdown")] [InlineData("application/octet-stream", "photo.JPG", "image/jpeg")]
    public void Mime_is_primary_with_generic_type_fallback(string mime, string name, string expected) => Assert.Equal(expected, FileContentTypes.Resolve(new() { ContentType = mime, Name = name }));
    public void Dispose() { db.Dispose(); connection.Dispose(); }
    private sealed class FakeStorage() : ObjectStorage(new MinioClient().WithEndpoint("localhost:9000").WithCredentials("test", "test").Build(), new ConfigurationBuilder().Build())
    {
        public (long, long)? Range; public bool Fail;
        public override Task CopyTo(string key, Stream destination, long offset, long length, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("Storage unavailable"); Range = (offset, length);
            return destination.WriteAsync(Enumerable.Range((int)offset, (int)length).Select(x => (byte)x).ToArray(), cancellationToken).AsTask();
        }
    }
}
