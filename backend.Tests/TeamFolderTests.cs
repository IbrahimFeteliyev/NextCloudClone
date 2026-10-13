using System.Security.Claims;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Minio;
using Xunit;
namespace Atlas.Tests;
public sealed class TeamFolderTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db; private readonly PermissionService permissions;
    private readonly User finance = new() { Name = "Finance employee", Email = "finance@test", RoleId = 1 };
    private readonly User hr = new() { Name = "HR employee", Email = "hr@test", RoleId = 1 };
    private readonly User admin = new() { Name = "Admin", Email = "admin@test", RoleId = 2 };
    private readonly User storageUser = new() { Name = "Team storage", Email = OrganizationService.StorageIdentity, RoleId = 1, IsActive = false };
    private readonly Department financeDepartment = new() { Name = "Finance" }; private readonly Department hrDepartment = new() { Name = "HR" };
    private readonly Group all = new() { Name = "All Staff", IsAllStaff = true }; private readonly Group admins = new() { Name = "Administrators", IsAdministrators = true };
    private readonly Group financeGroup; private readonly Group hrGroup; private readonly TeamSpace team = new() { Name = "Finance" };
    private readonly Folder root; private readonly Folder child; private readonly Folder common; private readonly Document file;
    public TeamFolderTests()
    {
        connection.Open(); db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options); db.Database.EnsureCreated();
        db.Roles.AddRange(new Role { Id = 1, Name = "Employee" }, new Role { Id = 2, Name = "Admin" });
        finance.DepartmentId = financeDepartment.Id; hr.DepartmentId = hrDepartment.Id;
        db.Departments.AddRange(financeDepartment, hrDepartment); db.Users.AddRange(finance, hr, admin, storageUser);
        financeGroup = new() { Name = "Finance", DepartmentId = financeDepartment.Id }; hrGroup = new() { Name = "HR", DepartmentId = hrDepartment.Id }; db.Groups.AddRange(all, admins, financeGroup, hrGroup);
        foreach (var user in new[] { finance, hr, admin }) db.UserGroups.Add(new() { UserId = user.Id, GroupId = all.Id });
        db.UserGroups.AddRange(new UserGroup { UserId = finance.Id, GroupId = financeGroup.Id }, new UserGroup { UserId = hr.Id, GroupId = hrGroup.Id }, new UserGroup { UserId = admin.Id, GroupId = admins.Id });
        db.TeamSpaces.Add(team); var commonTeam = new TeamSpace { Name = "All Staff" }; db.TeamSpaces.Add(commonTeam);
        root = new() { Name = "Finance", OwnerId = storageUser.Id, TeamSpaceId = team.Id, IsTeamRoot = true };
        child = new() { Name = "Reports", OwnerId = storageUser.Id, TeamSpaceId = team.Id, ParentFolderId = root.Id };
        common = new() { Name = "All Staff", OwnerId = storageUser.Id, TeamSpaceId = commonTeam.Id, IsTeamRoot = true };
        file = new() { Name = "budget.txt", ObjectKey = "budget", OwnerId = storageUser.Id, TeamSpaceId = team.Id, ParentFolderId = child.Id };
        db.Folders.AddRange(root, child, common); db.Files.Add(file);
        db.GroupPermissions.AddRange(new GroupPermission { GroupId = financeGroup.Id, FolderId = root.Id, Access = Access.All }, new GroupPermission { GroupId = admins.Id, FolderId = root.Id, Access = Access.All }, new GroupPermission { GroupId = all.Id, FolderId = common.Id, Access = Access.Read });
        db.SaveChanges(); permissions = new(db);
    }
    private CurrentUser Current(User user) => new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user == admin ? "Admin" : "Employee")], "test")) } });
    private DocumentService Documents(User user) => new(db, Current(user), permissions, new ObjectStorage(new MinioClient().WithEndpoint("unused:9000").WithCredentials("test", "password").Build(), new ConfigurationBuilder().Build()), new(db));
    private OrganizationService Organization(User user) => new(db, Current(user), permissions, new(db), new PasswordHasher<User>());
    [Fact] public async Task Common_documents_are_readable_but_department_content_is_isolated()
    {
        Assert.Equal(Access.Read, await permissions.FolderAccess(hr.Id, common.Id)); Assert.Equal(Access.All, await permissions.FileAccess(finance.Id, file.Id)); Assert.Equal(Access.None, await permissions.FileAccess(hr.Id, file.Id));
        var visible = await Documents(finance).Explore("teams", null, null); Assert.Equal(2, visible.Items.Count); Assert.All(visible.Items, x => Assert.True(x.IsTeamRoot));
        Assert.Single((await Documents(hr).Explore("teams", null, null)).Items);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Documents(hr).Explore("teams", child.Id, null))).Status);
    }
    [Fact] public async Task Individual_override_and_removal_replace_and_restore_group_access()
    {
        await Documents(finance).SetShare("file", file.Id, new(hr.Id, Access.Read)); Assert.Equal(Access.Read, await permissions.FileAccess(hr.Id, file.Id));
        await Documents(finance).SetShare("file", file.Id, new(hr.Id, Access.None)); Assert.Equal(Access.None, await permissions.FileAccess(hr.Id, file.Id));
        await Documents(finance).RemoveShare("file", file.Id, hr.Id); Assert.Equal(Access.None, await permissions.FileAccess(hr.Id, file.Id));
    }
    [Fact] public async Task Group_union_and_deny_apply_at_nearest_level_and_individual_override_wins()
    {
        var extra = new Group { Name = "Project" }; db.Groups.Add(extra); db.UserGroups.Add(new() { UserId = finance.Id, GroupId = extra.Id });
        db.GroupPermissions.AddRange(new GroupPermission { GroupId = financeGroup.Id, FolderId = child.Id, Access = Access.Read }, new GroupPermission { GroupId = extra.Id, FolderId = child.Id, Access = Access.Read | Access.Write }); await db.SaveChangesAsync();
        Assert.Equal(Access.Read | Access.Write, await permissions.FileAccess(finance.Id, file.Id));
        (await db.GroupPermissions.SingleAsync(x => x.GroupId == extra.Id)).Access = Access.None; await db.SaveChangesAsync(); Assert.Equal(Access.None, await permissions.FileAccess(finance.Id, file.Id));
        db.FilePermissions.Add(new() { FileId = file.Id, UserId = finance.Id, Access = Access.Read }); await db.SaveChangesAsync(); Assert.Equal(Access.Read, await permissions.FileAccess(finance.Id, file.Id));
    }
    [Fact] public async Task Department_changes_revoke_old_access_without_changing_role_or_content()
    {
        await Organization(admin).SetDepartment(finance.Id, hrDepartment.Id); await Organization(admin).SetDepartment(finance.Id, hrDepartment.Id);
        Assert.Equal(Access.None, await permissions.FileAccess(finance.Id, file.Id)); Assert.Equal(1, finance.RoleId); Assert.Equal(storageUser.Id, file.OwnerId); Assert.Single(await db.Files.ToListAsync());
        Assert.False(await db.UserGroups.AnyAsync(x => x.UserId == finance.Id && x.GroupId == financeGroup.Id)); Assert.True(await db.UserGroups.AnyAsync(x => x.UserId == finance.Id && x.GroupId == hrGroup.Id));
    }
    [Fact] public async Task Admin_role_does_not_bypass_resource_permissions_and_inactive_users_lose_access()
    {
        db.GroupPermissions.Remove(await db.GroupPermissions.SingleAsync(x => x.GroupId == admins.Id)); await db.SaveChangesAsync(); Assert.Equal(Access.None, await permissions.FileAccess(admin.Id, file.Id));
        await Organization(admin).SetActive(finance.Id, false); Assert.Equal(Access.None, await permissions.FileAccess(finance.Id, file.Id)); Assert.Equal(storageUser.Id, file.OwnerId);
    }
    [Fact] public async Task Employee_creation_assigns_all_staff_and_department_groups_and_admin_routes_are_protected()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Organization(finance).CreateGroup("Forbidden"))).Status);
        await Organization(admin).CreateUser("New employee", "new@test.local", "Password123!", financeDepartment.Id);
        var created = await db.Users.SingleAsync(x => x.Email == "new@test.local"); Assert.Equal(1, created.RoleId); Assert.Equal(financeDepartment.Id, created.DepartmentId);
        Assert.True(await db.UserGroups.AnyAsync(x => x.UserId == created.Id && x.GroupId == all.Id)); Assert.Equal(Access.All, await permissions.FileAccess(created.Id, file.Id)); Assert.True(await db.Folders.AnyAsync(x => x.OwnerId == created.Id && x.IsRoot));
    }
    [Fact] public async Task Team_upload_folders_and_trash_use_team_ownership_and_management_permissions()
    {
        var created = await Documents(finance).CreateFolder(new("New folder", root.Id)); Assert.Equal(team.Id, created.TeamSpaceId); Assert.Equal(storageUser.Id, created.OwnerId);
        await Documents(finance).Delete("file", file.Id); Assert.Equal(1, await db.Files.IgnoreQueryFilters().CountAsync());
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Documents(hr).RestoreTrash("file", file.Id))).Status);
        await Documents(finance).RestoreTrash("file", file.Id); Assert.Null(file.DeletedAt);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => Documents(finance).Delete("folder", root.Id))).Status);
    }
    [Fact] public async Task Extra_group_membership_grants_and_revokes_team_access_immediately()
    {
        await Organization(admin).CreateGroup("Project team");
        var group = await db.Groups.SingleAsync(x => x.Name == "Project team");
        await Organization(admin).CreateTeam("Project documents", group.Id, Access.Read | Access.Write);
        var folder = await db.Folders.SingleAsync(x => x.Name == "Project documents");
        Assert.True(folder.IsTeamRoot); Assert.NotNull(folder.TeamSpaceId); Assert.Equal(storageUser.Id, folder.OwnerId);
        Assert.Equal(Access.All, await permissions.FolderAccess(admin.Id, folder.Id));
        Assert.Equal(Access.None, await permissions.FolderAccess(hr.Id, folder.Id));
        await Organization(admin).SetMember(group.Id, hr.Id, true);
        Assert.Equal(Access.Read | Access.Write, await permissions.FolderAccess(hr.Id, folder.Id));
        await Organization(admin).SetMember(group.Id, hr.Id, false);
        Assert.Equal(Access.None, await permissions.FolderAccess(hr.Id, folder.Id));
    }
    [Fact] public async Task Group_sharing_requires_share_permission_and_cannot_escalate_access()
    {
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Organization(hr).SetGroupShare("file", file.Id, hrGroup.Id, Access.Read))).Status);
        await Organization(finance).SetGroupShare("file", file.Id, hrGroup.Id, Access.Read | Access.Share);
        Assert.Equal(Access.Read | Access.Share, await permissions.FileAccess(hr.Id, file.Id));
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => Organization(hr).SetGroupShare("file", file.Id, all.Id, Access.All))).Status);
        await Organization(finance).SetGroupShare("file", file.Id, hrGroup.Id, Access.None);
        Assert.Equal(Access.None, await permissions.FileAccess(hr.Id, file.Id));
        await Organization(finance).SetGroupShare("file", file.Id, hrGroup.Id, Access.None, remove: true);
        Assert.False(await db.GroupPermissions.AnyAsync(x => x.FileId == file.Id && x.GroupId == hrGroup.Id));
    }
    public void Dispose() { db.Dispose(); connection.Dispose(); }
}
