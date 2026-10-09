using System.Security.Claims;
using System.Text;
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
public sealed class TextAndUploadTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly User owner = new() { Name = "Owner", Email = "owner@test", RoleId = 1 };
    private readonly User reader = new() { Name = "Reader", Email = "reader@test", RoleId = 1 };
    private readonly Folder root; private readonly Document file;
    private readonly MemoryStorage storage = new(); private readonly Failure failure = new();
    private readonly PermissionService permissions; private readonly AuditService audit; private readonly TextContentService text;
    public TextAndUploadTests()
    {
        connection.Open(); db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).AddInterceptors(failure).Options); db.Database.EnsureCreated();
        db.Roles.Add(new() { Id = 1, Name = "Employee" }); db.Users.AddRange(owner, reader);
        root = new() { Name = "Documents", OwnerId = owner.Id };
        file = new() { Name = "notes.txt", OriginalName = "notes.txt", ParentFolderId = root.Id, OwnerId = owner.Id, ObjectKey = "original", Size = 8, ContentType = "text/plain" };
        db.Folders.Add(root); db.Files.Add(file); db.FileVersions.Add(FileVersionService.Initial(file, owner.Id));
        db.FolderPermissions.Add(new() { FolderId = root.Id, UserId = reader.Id, Access = Access.Read }); db.SaveChanges();
        storage.Objects["original"] = Encoding.UTF8.GetBytes("original"); permissions = new(db); audit = new(db);
        text = new(db, permissions, new(db, permissions, storage, audit), new(), storage, audit, NullLogger<TextContentService>.Instance);
    }
    private Task Save(Guid actor, string content = "updated", int version = 1, CancellationToken token = default) => text.Save(actor, file.Id, content, version, token);
    private DocumentService Documents(Guid actor) => new(db, new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test")) } }), permissions, storage, audit);
    private FormFile Upload() => new(new MemoryStream(Encoding.UTF8.GetBytes("upload")), 0, 6, "file", "upload.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" };
    private FormFile ReplacementUpload() => new(new MemoryStream(Encoding.UTF8.GetBytes("replacement")), 0, 11, "file", file.Name) { Headers = new HeaderDictionary(), ContentType = "text/plain" };
    [Fact] public async Task Upload_replace_requires_confirmation_and_preserves_identity_shares_and_saved_versions()
    {
        var originalId = file.Id;
        var conflict = await Assert.ThrowsAsync<ApiException>(() => Documents(owner.Id).Upload(root.Id, ReplacementUpload()));
        Assert.Equal(409, conflict.Status); Assert.NotNull(conflict.Details); Assert.Single(storage.Objects);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Documents(owner.Id).Upload(root.Id, ReplacementUpload(), default, true, file.Id, 99))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Documents(reader.Id).Upload(root.Id, ReplacementUpload(), default, true, file.Id, 1))).Status);
        var result = await Documents(owner.Id).Upload(root.Id, ReplacementUpload(), default, true, file.Id, 1);
        Assert.Equal(originalId, result.Id); Assert.Equal(owner.Id, file.OwnerId); Assert.Equal(root.Id, file.ParentFolderId);
        Assert.Single(await db.Files.ToListAsync()); Assert.Single(await db.FileVersions.ToListAsync()); Assert.Single(await db.FolderPermissions.ToListAsync());
        Assert.Equal("replacement", Encoding.UTF8.GetString(storage.Objects[file.ObjectKey])); Assert.True(storage.Objects.ContainsKey("original"));
    }
    [Fact] public async Task Failed_replace_preserves_current_content_and_saved_versions()
    {
        failure.Enabled = true;
        await Assert.ThrowsAsync<IOException>(() => Documents(owner.Id).Upload(root.Id, ReplacementUpload(), default, true, file.Id, 1));
        db.ChangeTracker.Clear(); Assert.Equal("original", (await db.Files.SingleAsync()).ObjectKey); Assert.Single(storage.Objects); Assert.Single(await db.FileVersions.ToListAsync());
    }
    [Fact] public async Task Upload_cannot_replace_a_folder()
    {
        db.Folders.Add(new() { Name = file.Name, ParentFolderId = root.Id, OwnerId = owner.Id }); await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Documents(owner.Id).Upload(root.Id, ReplacementUpload(), default, true, file.Id, 1))).Status);
        Assert.Equal(1, file.Version);
    }
    [Fact] public async Task Read_only_and_missing_files_cannot_be_saved()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(reader.Id))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => text.Save(owner.Id, Guid.NewGuid(), "x", 1, default))).Status);
        Assert.Single(storage.Objects);
    }
    [Fact] public async Task Inherited_write_saves_content_without_automatic_snapshot()
    {
        (await db.FolderPermissions.SingleAsync()).Access = Access.Read | Access.Write; await db.SaveChangesAsync();
        var created = file.CreatedAt;
        await Save(reader.Id, "Azərbaycan\r\nSecond line\r\n");
        Assert.Equal(2, file.Version); Assert.Equal(created, file.CreatedAt); Assert.Equal(owner.Id, file.OwnerId);
        Assert.Equal(root.Id, file.ParentFolderId); Assert.Equal("notes.txt", file.Name); Assert.Equal("notes.txt", file.OriginalName); Assert.Equal("text/plain", file.ContentType);
        Assert.Equal("Azərbaycan\r\nSecond line\r\n", Encoding.UTF8.GetString(storage.Objects[file.ObjectKey]));
        Assert.Equal("original", Encoding.UTF8.GetString(storage.Objects["original"])); Assert.Equal(1, await db.FileVersions.CountAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "EDIT_FILE" && x.UserId == reader.Id);
    }
    [Fact] public async Task File_override_and_revoked_write_win_over_folder_write()
    {
        (await db.FolderPermissions.SingleAsync()).Access = Access.All;
        db.FilePermissions.Add(new() { FileId = file.Id, UserId = reader.Id, Access = Access.Read }); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(reader.Id))).Status);
    }
    [Fact] public async Task Stale_save_does_not_overwrite_newer_content()
    {
        await Save(owner.Id); Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id, "stale"))).Status);
        Assert.Equal(2, file.Version); Assert.Equal(2, storage.Objects.Count);
    }
    [Theory] [InlineData("notes.rtf", "application/rtf", 415)] [InlineData("notes.txt", "application/pdf", 415)]
    public async Task Unsupported_types_cannot_use_text_save(string name, string mime, int status)
    {
        file.Name = name; file.ContentType = mime; await db.SaveChangesAsync();
        Assert.Equal(status, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id))).Status);
    }
    [Fact] public async Task All_registry_types_save_without_automatic_snapshots_and_preserve_permissions()
    {
        var id = file.Id;
        foreach (var type in TextFileTypes.All)
            foreach (var extension in type.Extensions)
            {
                file.Name = "source." + extension; file.ContentType = type.Mime; await db.SaveChangesAsync();
                var version = file.Version;
                const string source = "<script>alert('display only')</script>\r\n";
                await Save(owner.Id, source, version);
                Assert.Equal(id, file.Id); Assert.Equal(version + 1, file.Version);
                Assert.Equal(source, Encoding.UTF8.GetString(storage.Objects[file.ObjectKey]));
                Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Save(reader.Id, "forbidden", file.Version))).Status);
            }
        Assert.Equal(1, await db.FileVersions.CountAsync());
    }
    [Fact] public void Generic_and_desktop_text_mimes_normalize_through_shared_registry()
    {
        foreach (var type in TextFileTypes.All)
            foreach (var mime in type.Aliases.Append("application/octet-stream").Append("text/plain").Append(type.Mime))
                Assert.Equal(type.Mime, FileContentTypes.Resolve(new Document { Name = "file." + type.Extensions[0], ContentType = mime }));
        Assert.Null(TextFileTypes.Resolve("file.json", "image/png"));
        Assert.Null(TextFileTypes.Resolve("file.html", "application/pdf"));
        Assert.Null(TextFileTypes.Resolve("file.zip", "text/plain"));
    }
    [Fact] public async Task Existing_and_updated_byte_limits_are_enforced()
    {
        Assert.Equal(413, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id, new string('ə', TextContentService.MaxBytes / 2 + 1)))).Status);
        file.Size = TextContentService.MaxBytes + 1; await db.SaveChangesAsync();
        Assert.Equal(413, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id))).Status); Assert.Single(storage.Objects);
    }
    [Fact] public async Task Empty_text_is_a_valid_version()
    {
        await Save(owner.Id, ""); Assert.Equal(0, file.Size); Assert.Empty(storage.Objects[file.ObjectKey]); Assert.Equal(2, file.Version);
    }
    [Fact] public async Task Non_utf8_original_is_protected()
    {
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id, "binary\0text"))).Status);
        storage.Objects["original"] = [0xff, 0xfe, 65, 0]; Assert.Equal(415, (await Assert.ThrowsAsync<ApiException>(() => Save(owner.Id))).Status);
        Assert.Equal(1, file.Version);
    }
    [Theory] [InlineData(false)] [InlineData(true)] public async Task Failed_save_keeps_original_and_removes_new_object(bool databaseFailure)
    {
        failure.Enabled = databaseFailure; storage.FailPut = !databaseFailure;
        await Assert.ThrowsAsync<IOException>(() => Save(owner.Id)); db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Files.SingleAsync()).Version); Assert.Single(await db.FileVersions.ToListAsync()); Assert.Single(storage.Objects);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
    [Fact] public async Task Cancelled_upload_does_not_create_metadata_or_audit()
    {
        using var cancel = new CancellationTokenSource(); storage.AfterPut = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Documents(owner.Id).Upload(root.Id, Upload(), cancel.Token));
        db.ChangeTracker.Clear(); Assert.Single(await db.Files.ToListAsync()); Assert.Single(await db.FileVersions.ToListAsync()); Assert.Empty(await db.AuditLogs.ToListAsync()); Assert.Single(storage.Objects);
    }
    [Theory] [InlineData(false)] [InlineData(true)] public async Task Failed_upload_cleans_object_and_does_not_publish_metadata(bool databaseFailure)
    {
        failure.Enabled = databaseFailure; storage.FailPut = !databaseFailure;
        await Assert.ThrowsAsync<IOException>(() => Documents(owner.Id).Upload(root.Id, Upload())); db.ChangeTracker.Clear();
        Assert.Single(await db.Files.ToListAsync()); Assert.Single(await db.FileVersions.ToListAsync()); Assert.Empty(await db.AuditLogs.ToListAsync()); Assert.Single(storage.Objects);
    }
    [Fact] public async Task Upload_checks_folder_write_and_commits_resource_version_and_audit_together()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Documents(reader.Id).Upload(root.Id, Upload()))).Status);
        var resource = await Documents(owner.Id).Upload(root.Id, Upload()); Assert.Equal("upload.txt", resource.Name);
        Assert.Equal(2, await db.Files.CountAsync()); Assert.Equal(1, await db.FileVersions.CountAsync()); Assert.Single(await db.AuditLogs.ToListAsync());
    }
    private sealed class Failure : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (Enabled) throw new IOException("Database outage"); return ValueTask.FromResult(result); }
    }
    private sealed class MemoryStorage() : ObjectStorage(new MinioClient().WithEndpoint("unused:9000").WithCredentials("test", "test-only").Build(), new ConfigurationBuilder().Build())
    {
        public readonly Dictionary<string, byte[]> Objects = new(); public bool FailPut; public Action? AfterPut;
        public override async Task Put(string key, Stream stream, long size, string mime, CancellationToken token = default)
        {
            using var data = new MemoryStream(); await stream.CopyToAsync(data, token); Objects[key] = data.ToArray();
            if (FailPut) throw new IOException("Partial storage write"); AfterPut?.Invoke();
        }
        public override Task<MemoryStream> Get(string key) => Task.FromResult(new MemoryStream(Objects[key]));
        public override Task Remove(string key) { Objects.Remove(key); return Task.CompletedTask; }
    }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
