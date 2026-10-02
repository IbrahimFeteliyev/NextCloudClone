using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Minio;
using Xunit;

namespace Atlas.Tests;
public sealed class PermissionTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;
    private readonly PermissionService permissions;
    private readonly User finance = new() { Email = "finance@demo.local", Name = "Finance", RoleId = 1 };
    private readonly User manager = new() { Email = "manager@demo.local", Name = "Manager", RoleId = 1 };
    private readonly User admin = new() { Email = "admin@demo.local", Name = "Admin", RoleId = 2 };
    private readonly Folder root; private readonly Folder budget; private readonly Document file;
    public PermissionTests()
    {
        connection.Open(); db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        db.Roles.AddRange(new Role { Id = 1, Name = "Employee" }, new Role { Id = 2, Name = "Admin" }); db.Users.AddRange(finance, manager, admin);
        var personal = new Folder { Name = "My Files", OwnerId = finance.Id, IsRoot = true };
        root = new() { Name = "Finance", OwnerId = finance.Id, ParentFolderId = personal.Id };
        budget = new() { Name = "Budget", OwnerId = finance.Id, ParentFolderId = root.Id };
        file = new() { Name = "Budget_2027.xlsx", OriginalName = "Budget_2027.xlsx", OwnerId = finance.Id, ParentFolderId = budget.Id, ObjectKey = "documents/test" };
        db.Folders.AddRange(personal, root, budget); db.Files.Add(file); db.SaveChanges(); permissions = new(db);
    }
    private async Task Grant(Guid folder, Access access) { db.FolderPermissions.Add(new() { FolderId = folder, UserId = manager.Id, Access = access }); await db.SaveChangesAsync(); }
    private DocumentService Service(User actor)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.Id.ToString())], "test");
        var current = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });
        var config = new ConfigurationBuilder().Build();
        var storage = new ObjectStorage(new MinioClient().WithEndpoint("localhost:9000").WithCredentials("test", "test-password").Build(), config);
        return new(db, current, permissions, storage, new AuditService(db));
    }
    [Fact] public async Task Read_folder_is_inherited_by_nested_folder_and_file()
    { await Grant(root.Id, Access.Read); Assert.Equal(Access.Read, await permissions.FolderAccess(manager.Id, budget.Id)); Assert.Equal(Access.Read, await permissions.FileAccess(manager.Id, file.Id)); }
    [Fact] public async Task Nearest_folder_override_replaces_parent_grant()
    { await Grant(root.Id, Access.All); await Grant(budget.Id, Access.Read | Access.Write); Assert.Equal(Access.Read | Access.Write, await permissions.FileAccess(manager.Id, file.Id)); }
    [Fact] public async Task Explicit_none_blocks_inherited_access()
    { await Grant(root.Id, Access.Read); await Grant(budget.Id, Access.None); Assert.Equal(Access.None, await permissions.FileAccess(manager.Id, file.Id)); }
    [Fact] public async Task File_override_replaces_folder_grant()
    { await Grant(root.Id, Access.All); db.FilePermissions.Add(new() { FileId = file.Id, UserId = manager.Id, Access = Access.Read }); await db.SaveChangesAsync(); Assert.Equal(Access.Read, await permissions.FileAccess(manager.Id, file.Id)); }
    [Fact] public async Task Owners_have_full_access_and_admins_do_not_bypass_sharing()
    { Assert.Equal(Access.All, await permissions.FileAccess(finance.Id, file.Id)); Assert.Equal(Access.None, await permissions.FileAccess(admin.Id, file.Id)); }
    [Fact] public async Task Read_only_manager_cannot_create_rename_or_delete()
    {
        await Grant(root.Id, Access.Read); var service = Service(manager);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.CreateFolder(new("New", budget.Id)))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.Rename("file", file.Id, "Changed.xlsx"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.Delete("folder", budget.Id))).Status);
    }
    [Fact] public async Task Updated_write_access_allows_creation_and_preserves_workspace_ownership()
    {
        await Grant(root.Id, Access.Read); var grant = await db.FolderPermissions.SingleAsync(); grant.Access = Access.Read | Access.Write; await db.SaveChangesAsync();
        var created = await Service(manager).CreateFolder(new("Forecast", budget.Id));
        Assert.Equal(finance.Id, created.OwnerId); Assert.Equal(Access.Read | Access.Write, created.Permissions); Assert.Equal(manager.Id, (await db.AuditLogs.SingleAsync()).UserId);
    }
    [Fact] public async Task Share_permission_cannot_delegate_permissions_the_actor_does_not_have()
    {
        await Grant(root.Id, Access.Read | Access.Share);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(manager).SetShare("folder", root.Id, new(admin.Id, Access.All)))).Status);
    }
    [Fact] public async Task Removing_override_restores_parent_inheritance()
    {
        await Grant(root.Id, Access.Read); await Grant(budget.Id, Access.Read | Access.Write);
        await Service(finance).RemoveShare("folder", budget.Id, manager.Id); Assert.Equal(Access.Read, await permissions.FolderAccess(manager.Id, budget.Id));
    }
    [Fact] public async Task Shared_view_reveals_shared_folder_without_leaking_personal_root()
    {
        await Grant(root.Id, Access.Read); var view = await Service(manager).Explore("shared", null, null);
        Assert.Single(view.Items); Assert.Equal(root.Id, view.Items[0].Id);
        var nested = await Service(manager).Explore("shared", budget.Id, null);
        Assert.Equal(new[] { "Finance", "Budget" }, nested.Breadcrumbs.Select(x => x.Name)); Assert.Single(nested.Items);
    }
    [Fact] public async Task More_specific_delete_denial_prevents_recursive_delete()
    {
        await Grant(root.Id, Access.Read | Access.Delete); db.FilePermissions.Add(new() { FileId = file.Id, UserId = manager.Id, Access = Access.Read }); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(manager).Delete("folder", root.Id))).Status);
        Assert.True(await db.Folders.AnyAsync(x => x.Id == root.Id)); Assert.True(await db.Files.AnyAsync(x => x.Id == file.Id));
    }
    [Fact] public async Task Move_rejects_folder_cycle()
    { Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Service(finance).Move("folder", root.Id, budget.Id))).Status); }
    [Fact] public async Task Duplicate_names_fail_without_creating_an_audit_event()
    { Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Service(finance).CreateFolder(new("Budget", root.Id)))).Status); Assert.Empty(await db.AuditLogs.ToListAsync()); }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
