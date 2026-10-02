using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Minio;
using Xunit;

namespace Atlas.Tests;
public sealed class OnlyOfficeTests : IDisposable
{
    private static readonly string Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly User owner = new() { Email = "owner@test.local", Name = "Owner", RoleId = 1 };
    private readonly User reader = new() { Email = "reader@test.local", Name = "Reader", RoleId = 1 };
    private readonly User stranger = new() { Email = "stranger@test.local", Name = "Stranger", RoleId = 1 };
    private readonly Folder root; private readonly Folder child; private readonly Document file;
    private readonly PermissionService permissions; private readonly AuditService audit; private readonly MemoryStorage storage = new();
    private readonly DocumentLocks locks = new(); private readonly OnlyOfficeTokens tokens; private readonly OnlyOfficeOptions options;
    private readonly FakeServer server = new(); private readonly OnlyOfficeClient client;
    private readonly FileVersionService versions;
    private readonly SaveFailure saveFailure = new();
    public OnlyOfficeTests()
    {
        Environment.SetEnvironmentVariable("OnlyOffice__JwtSecret", Secret);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OnlyOffice:DocumentServerUrl"] = "http://office.test:8082", ["OnlyOffice:InternalUrl"] = "http://office.test:8082",
            ["OnlyOffice:ApiUrl"] = "http://api.test:5050"
        }).Build();
        options = new(config); tokens = new(options); client = new(new HttpClient(server), options);
        connection.Open(); db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(saveFailure).Options); db.Database.EnsureCreated();
        db.Roles.Add(new() { Id = 1, Name = "Employee" }); db.Users.AddRange(owner, reader, stranger);
        root = new() { Name = "Finance", OwnerId = owner.Id };
        child = new() { Name = "Budget", OwnerId = owner.Id, ParentFolderId = root.Id };
        file = new() { Name = "Budget.xlsx", OriginalName = "Budget.xlsx", OwnerId = owner.Id, ParentFolderId = child.Id,
            ObjectKey = "documents/original", ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Size = 4 };
        db.Folders.AddRange(root, child); db.Files.Add(file); db.FileVersions.Add(FileVersionService.Initial(file, owner.Id));
        db.FolderPermissions.Add(new() { FolderId = root.Id, UserId = reader.Id, Access = Access.Read }); db.SaveChanges();
        permissions = new(db); audit = new(db); versions = new(db, permissions, storage, audit);
        storage.Objects[file.ObjectKey] = Encoding.UTF8.GetBytes("original");
    }
    private OnlyOfficeService Service(User user)
    {
        var current = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test")) } });
        return new(db, current, permissions, storage, versions, audit, options, tokens, client, locks, NullLogger<OnlyOfficeService>.Instance);
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static string AccessToken(JsonElement config, string property) => new Uri(config.GetProperty("editorConfig").GetProperty(property).GetString()!).Query.Split("access_token=")[1];
    private static string Key(JsonElement config) => config.GetProperty("document").GetProperty("key").GetString()!;
    private async Task WriteGrant(Access access) { (await db.FolderPermissions.SingleAsync()).Access = access; await db.SaveChangesAsync(); }
    private Task Save(JsonElement config, int status, User actor, string url = "http://office.test:8082/cache/files/saved/output.xlsx")
    {
        var signed = tokens.Sign(new { key = Key(config), status, url, filetype = "xlsx", users = new[] { actor.Id.ToString() } }, DateTime.UtcNow.AddMinutes(5));
        return Service(owner).Callback(file.Id, AccessToken(config, "callbackUrl"), Json(new { token = signed }), null);
    }
    [Fact] public async Task Inherited_read_access_is_signed_view_only_and_cannot_request_edit()
    {
        var response = await Service(reader).Config(file.Id, "auto"); var config = Json(response.Config);
        Assert.Equal("view", response.Mode); Assert.False(config.GetProperty("document").GetProperty("permissions").GetProperty("edit").GetBoolean());
        Assert.True(config.GetProperty("document").GetProperty("permissions").GetProperty("download").GetBoolean());
        Assert.True(config.GetProperty("document").GetProperty("permissions").GetProperty("print").GetBoolean());
        var signed = tokens.Validate(config.GetProperty("token").GetString(), true);
        Assert.Equal("view", signed.GetProperty("editorConfig").GetProperty("mode").GetString());
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(reader).Config(file.Id, "edit"))).Status);
    }
    [Theory] [InlineData("Contract.DOCX", "word")] [InlineData("Budget.xlsx", "cell")] [InlineData("Review.pptx", "slide")]
    public async Task Supported_types_and_owner_edit_mode(string name, string type)
    {
        file.Name = name; await db.SaveChangesAsync(); var response = await Service(owner).Config(file.Id, "auto");
        Assert.Equal("edit", response.Mode); Assert.Equal(type, Json(response.Config).GetProperty("documentType").GetString());
    }
    [Fact] public async Task Explicit_file_deny_overrides_shared_folder_write()
    {
        await WriteGrant(Access.Read | Access.Write); db.FilePermissions.Add(new() { FileId = file.Id, UserId = reader.Id, Access = Access.None }); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(reader).Config(file.Id, "auto"))).Status);
    }
    [Fact] public async Task Missing_unsupported_and_unauthorized_files_are_rejected()
    {
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Config(Guid.NewGuid(), "auto"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(stranger).Config(file.Id, "auto"))).Status);
        file.Name = "Notes.txt"; await db.SaveChangesAsync(); Assert.Equal(415, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Config(file.Id, "auto"))).Status);
    }
    [Fact] public async Task Concurrent_editors_and_readers_share_one_key()
    {
        await WriteGrant(Access.Read | Access.Write);
        var first = Json((await Service(owner).Config(file.Id, "auto")).Config); var second = Json((await Service(reader).Config(file.Id, "auto")).Config);
        Assert.Equal(Key(first), Key(second)); Assert.Single(await db.OfficeSessions.ToListAsync());
    }
    [Fact] public async Task Download_token_is_scoped_and_rechecks_revoked_read_access()
    {
        var config = Json((await Service(reader).Config(file.Id, "auto")).Config);
        var token = new Uri(config.GetProperty("document").GetProperty("url").GetString()!).Query.Split("access_token=")[1];
        using var content = (await Service(owner).Content(file.Id, token)).Stream; Assert.Equal("original", Encoding.UTF8.GetString(content.ToArray()));
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Content(Guid.NewGuid(), token))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Content(file.Id, token + "bad"))).Status);
        await WriteGrant(Access.None); Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Content(file.Id, token))).Status);
    }
    [Fact] public async Task Force_save_keeps_collaboration_key_final_save_rotates_and_preserves_metadata()
    {
        await WriteGrant(Access.Read | Access.Write); var config = Json((await Service(reader).Config(file.Id, "auto")).Config);
        var initialKey = file.ObjectKey; var createdAt = file.CreatedAt;
        await Save(config, 6, reader); Assert.Equal(2, file.Version); Assert.NotEqual(initialKey, file.ObjectKey);
        Assert.Equal(Key(config), Key(Json((await Service(owner).Config(file.Id, "auto")).Config)));
        await Save(config, 6, reader); Assert.Equal(2, file.Version); // Retries are idempotent.
        await Save(config, 2, reader); Assert.Equal(3, file.Version);
        await Save(config, 2, reader); Assert.Equal(3, file.Version);
        Assert.NotEqual(Key(config), Key(Json((await Service(owner).Config(file.Id, "auto")).Config)));
        Assert.Equal(owner.Id, file.OwnerId); Assert.Equal(child.Id, file.ParentFolderId); Assert.Equal("Budget.xlsx", file.Name);
        Assert.Equal("Budget.xlsx", file.OriginalName); Assert.Equal(createdAt, file.CreatedAt); Assert.Equal(3, await db.FileVersions.CountAsync());
        Assert.Equal(initialKey, (await db.FileVersions.SingleAsync(x => x.Number == 1)).ObjectKey);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "SAVE_FILE_VERSION" && x.UserId == reader.Id));
    }
    [Fact] public async Task Read_only_and_revoked_editors_cannot_save()
    {
        var config = Json((await Service(reader).Config(file.Id, "auto")).Config);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, reader))).Status);
        await WriteGrant(Access.Read | Access.Write); config = Json((await Service(reader).Config(file.Id, "auto")).Config);
        await WriteGrant(Access.Read); Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, reader))).Status);
        Assert.Equal(1, file.Version); Assert.Single(await db.FileVersions.ToListAsync()); Assert.Equal(0, server.Downloads);
    }
    [Fact] public async Task Config_jwt_cannot_be_replayed_as_callback_and_invalid_signatures_fail()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config); var grant = AccessToken(config, "callbackUrl");
        var body = Json(new { token = config.GetProperty("token").GetString(), key = Key(config), status = 2 });
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Callback(file.Id, grant, body, null))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Callback(file.Id, grant, Json(new { token = "invalid" }), null))).Status);
        Assert.Equal(1, file.Version);
    }
    [Fact] public async Task Only_signed_callback_fields_are_used()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        var signed = tokens.Sign(new { key = Key(config), status = 1 }, DateTime.UtcNow.AddMinutes(5));
        await Service(owner).Callback(file.Id, AccessToken(config, "callbackUrl"), Json(new { token = signed, status = 2, url = "http://evil.test/file", users = new[] { owner.Id.ToString() } }), null);
        Assert.Equal(1, file.Version); Assert.Equal(0, server.Downloads);
    }
    [Fact] public async Task Header_wrapped_callback_and_no_change_close_work()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        var signed = tokens.Sign(new { payload = new { key = Key(config), status = 4 } }, DateTime.UtcNow.AddMinutes(5));
        await Service(owner).Callback(file.Id, AccessToken(config, "callbackUrl"), Json(new { status = 4 }), "Bearer " + signed);
        Assert.NotNull((await db.OfficeSessions.SingleAsync()).ClosedAt);
        Assert.NotEqual(Key(config), Key(Json((await Service(owner).Config(file.Id, "auto")).Config)));
    }
    [Fact] public async Task Save_url_cannot_target_other_hosts_or_metadata_services()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        foreach (var url in new[] { "http://169.254.169.254/latest/meta-data", "http://evil.test/cache/files/saved/x.xlsx", "http://office.test:8082/admin" })
            Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner, url))).Status);
        Assert.Equal(0, server.Downloads); Assert.Equal(1, file.Version);
    }
    [Fact] public async Task Unavailable_server_and_failed_or_invalid_download_preserve_original()
    {
        server.Available = false; Assert.Equal(503, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Config(file.Id, "auto"))).Status);
        server.Available = true; var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        server.FailDownload = true; Assert.Equal(502, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner))).Status);
        server.FailDownload = false; server.Content = Encoding.UTF8.GetBytes("not an Office document");
        Assert.Equal(502, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner))).Status);
        Assert.Equal(1, file.Version); Assert.Equal("documents/original", file.ObjectKey);
    }
    [Fact] public async Task Failed_storage_write_does_not_advance_version()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config); storage.FailPut = true;
        await Assert.ThrowsAsync<IOException>(() => Save(config, 2, owner)); Assert.Equal(1, file.Version); Assert.Single(await db.FileVersions.ToListAsync());
    }
    [Fact] public async Task Closed_session_rejects_new_save_callbacks()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config); await Save(config, 2, owner);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner, "http://office.test:8082/cache/files/another/output.xlsx"))).Status);
    }
    [Fact] public async Task Expired_content_capabilities_and_sessions_are_rejected()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        var expired = tokens.Sign(new { purpose = "office-download", fileId = file.Id.ToString(), userId = owner.Id.ToString(), key = Key(config), version = 1 }, DateTime.UtcNow.AddMinutes(-5));
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => Service(owner).Content(file.Id, expired))).Status);
        (await db.OfficeSessions.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddMinutes(-5); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner))).Status);
    }
    [Fact] public async Task Snapshot_history_requires_current_read_permission()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => versions.List(stranger.Id, file.Id))).Status);
        await WriteGrant(Access.None); Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => versions.Download(reader.Id, file.Id, 1))).Status);
    }
    [Fact] public async Task Content_changed_outside_session_cannot_be_overwritten()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config); file.Version++; await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Save(config, 2, owner))).Status);
        Assert.Single(storage.Objects); Assert.Single(await db.FileVersions.ToListAsync());
    }
    [Fact] public async Task Database_failure_rolls_back_snapshot_and_cleans_up_the_new_object()
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config); saveFailure.Enabled = true;
        await Assert.ThrowsAsync<IOException>(() => Save(config, 2, owner)); Assert.Single(storage.Objects);
        saveFailure.Enabled = false; db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Files.SingleAsync()).Version); Assert.Single(await db.FileVersions.ToListAsync());
        Assert.Null((await db.OfficeSessions.SingleAsync()).ClosedAt);
        Assert.Equal(0, await db.AuditLogs.CountAsync(x => x.Action == "SAVE_FILE_VERSION"));
    }
    [Theory] [InlineData(3)] [InlineData(7)]
    public async Task Docs_save_error_preserves_the_previous_version(int status)
    {
        var config = Json((await Service(owner).Config(file.Id, "auto")).Config);
        Assert.Equal(502, (await Assert.ThrowsAsync<ApiException>(() => Save(config, status, owner))).Status);
        Assert.Equal(1, file.Version); Assert.Equal(0, server.Downloads);
    }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Enabled) throw new IOException("Simulated database outage"); return ValueTask.FromResult(result); }
    }
    private sealed class MemoryStorage() : ObjectStorage(new MinioClient().WithEndpoint("unused:9000").WithCredentials("test", "test-only").Build(), new ConfigurationBuilder().Build())
    {
        public readonly Dictionary<string, byte[]> Objects = new(); public bool FailPut;
        public override async Task Put(string key, Stream stream, long size, string contentType) { if (FailPut) throw new IOException("Storage unavailable"); using var data = new MemoryStream(); await stream.CopyToAsync(data); Objects[key] = data.ToArray(); }
        public override Task<MemoryStream> Get(string key) => Task.FromResult(new MemoryStream(Objects[key]));
        public override Task Remove(string key) { Objects.Remove(key); return Task.CompletedTask; }
    }
    private sealed class FakeServer : HttpMessageHandler
    {
        public bool Available = true, FailDownload; public int Downloads; public byte[] Content = OfficeBytes();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/healthcheck") return Task.FromResult(new HttpResponseMessage(Available ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable) { Content = new StringContent("true") });
            Downloads++; return Task.FromResult(new HttpResponseMessage(FailDownload ? HttpStatusCode.BadGateway : HttpStatusCode.OK) { Content = new ByteArrayContent(Content) });
        }
        private static byte[] OfficeBytes()
        {
            using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                foreach (var part in new[] { "[Content_Types].xml", "xl/workbook.xml" }) { using var writer = new StreamWriter(zip.CreateEntry(part).Open()); writer.Write("<test/>"); }
            return stream.ToArray();
        }
    }
}
