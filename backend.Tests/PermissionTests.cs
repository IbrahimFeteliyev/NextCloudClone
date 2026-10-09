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
    [Fact] public async Task Trash_preserves_tree_and_grants_blocks_content_and_restores_after_30_days()
    {
        await Grant(root.Id, Access.Read);
        var owner = Service(finance); await owner.SetFavorite("file", file.Id, true);
        await owner.Delete("folder", root.Id);
        Assert.Empty(await db.Files.ToListAsync()); Assert.DoesNotContain(await db.Folders.ToListAsync(), x => x.Id == root.Id);
        Assert.Equal(1, await db.Files.IgnoreQueryFilters().CountAsync()); Assert.Single(await db.Favorites.ToListAsync());
        Assert.Single(await db.FolderPermissions.ToListAsync());
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => permissions.FileAccess(finance.Id, file.Id))).Status);
        file.DeletedAt = DateTime.UtcNow.AddDays(-40); await db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(manager).RestoreTrash("folder", root.Id))).Status);
        await Service(finance).RestoreTrash("folder", root.Id);
        Assert.Single(await db.Files.ToListAsync()); Assert.Null(file.DeletedAt); Assert.Null(budget.DeletedAt);
        Assert.Equal(Access.Read, await permissions.FileAccess(manager.Id, file.Id));
    }
    [Fact] public async Task Trash_restore_rejects_name_conflicts_without_losing_deleted_content()
    {
        await Service(finance).Delete("file", file.Id);
        db.Files.Add(new() { Name = file.Name, OwnerId = finance.Id, ParentFolderId = budget.Id, ObjectKey = "documents/replacement" }); await db.SaveChangesAsync();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => Service(finance).RestoreTrash("file", file.Id))).Status);
        Assert.NotNull(file.DeletedAt); Assert.Equal(2, await db.Files.IgnoreQueryFilters().CountAsync());
    }
    [Fact] public async Task Recent_pagination_has_no_30_item_cap_and_keeps_permissions()
    {
        for (var i = 0; i < 45; i++) db.Files.Add(new() { Name = $"File-{i}", OwnerId = finance.Id, ParentFolderId = budget.Id, ObjectKey = $"documents/{i}", UpdatedAt = DateTime.UtcNow.AddMinutes(i) });
        await db.SaveChangesAsync();
        var first = await Service(finance).Explore("recent", null, null, 1, 20);
        var second = await Service(finance).Explore("recent", null, null, 2, 20);
        Assert.Equal(46, first.Total); Assert.Equal(20, first.Items.Count); Assert.Equal(20, second.Items.Count);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Equal(6, (await Service(finance).Explore("recent", null, null, 3, 20)).Items.Count);
        Assert.Empty((await Service(manager).Explore("recent", null, null, 1, 20)).Items);
    }
    [Fact] public async Task Read_folder_is_inherited_by_nested_folder_and_file()
    { await Grant(root.Id, Access.Read); Assert.Equal(Access.Read, await permissions.FolderAccess(manager.Id, budget.Id)); Assert.Equal(Access.Read, await permissions.FileAccess(manager.Id, file.Id)); }
    [Fact] public async Task Favorites_are_personal_idempotent_and_available_to_read_only_users()
    {
        await Grant(root.Id, Access.Read);
        var reader = Service(manager);
        await reader.SetFavorite("file", file.Id, true); await reader.SetFavorite("file", file.Id, true);
        await reader.SetFavorite("folder", budget.Id, true);
        Assert.Equal(2, await db.Favorites.CountAsync());
        var favoriteView = await reader.Explore("favorites", null, null);
        Assert.Equal(2, favoriteView.Items.Count); Assert.All(favoriteView.Items, item => Assert.True(item.IsFavorite));
        Assert.Empty((await Service(finance).Explore("favorites", null, null)).Items);
        Assert.False((await Service(finance).Explore("files", budget.Id, null)).Items.Single().IsFavorite);
        reader = Service(manager);
        Assert.Single((await reader.Explore("favorites", null, "xlsx")).Items);
        Assert.Single((await reader.Explore("favorites", budget.Id, null)).Items);
        await reader.SetFavorite("file", file.Id, false); await reader.SetFavorite("file", file.Id, false);
        Assert.Single((await reader.Explore("favorites", null, null)).Items);
    }
    [Fact] public async Task Favorites_do_not_bypass_revocation_and_are_cleaned_up_with_resources()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Service(manager).SetFavorite("file", file.Id, true))).Status);
        await Grant(root.Id, Access.Read); await Service(manager).SetFavorite("file", file.Id, true);
        (await db.FolderPermissions.SingleAsync()).Access = Access.None; await db.SaveChangesAsync();
        Assert.Empty((await Service(manager).Explore("favorites", null, null)).Items);
        db.Files.Remove(file); await db.SaveChangesAsync(); Assert.Empty(await db.Favorites.ToListAsync());
    }
    [Fact] public async Task Shared_by_me_lists_owned_active_direct_shares_and_respects_removal()
    {
        await Grant(root.Id, Access.Read);
        db.FilePermissions.Add(new() { FileId = file.Id, UserId = admin.Id, Access = Access.None }); await db.SaveChangesAsync();
        Assert.Equal(root.Id, (await Service(finance).Explore("shared-by-me", null, null)).Items.Single().Id);
        Assert.Empty((await Service(manager).Explore("shared-by-me", null, null)).Items);
        Assert.Empty((await Service(finance).Explore("shared-by-me", null, "xlsx")).Items);
        await Service(finance).SetShare("file", file.Id, new(admin.Id, Access.Read));
        Assert.Equal(2, (await Service(finance).Explore("shared-by-me", null, null)).Items.Count);
        Assert.Single((await Service(finance).Explore("shared-by-me", root.Id, null)).Items);
        await Service(finance).RemoveShare("folder", root.Id, manager.Id);
        Assert.Equal(file.Id, (await Service(finance).Explore("shared-by-me", null, null)).Items.Single().Id);
    }
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
