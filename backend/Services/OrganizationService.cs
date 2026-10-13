using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;
public class OrganizationService(AppDbContext db, CurrentUser current, PermissionService permissions, AuditService audit, PasswordHasher<User> hasher)
{
    public const string StorageIdentity = "team-storage@demo.local";
    public async Task RequireAdmin()
    {
        if (!await db.Users.AnyAsync(x => x.Id == current.Id && x.IsActive && x.Role.Name == "Admin")) throw new ApiException(403, "Administrator access is required.");
    }
    public async Task<object> Catalog() => new {
        departments = await db.Departments.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(),
        groups = await db.Groups.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.DepartmentId, x.IsAllStaff, x.IsAdministrators }).ToListAsync()
    };
    public async Task<object> Administration()
    {
        await RequireAdmin();
        return new { catalog = await Catalog(),
            users = await db.Users.Where(x => x.Email != StorageIdentity).OrderBy(x => x.Name).Select(x => new UserDto(x.Id, x.Email, x.Name, x.Role.Name, x.DepartmentId, x.Department == null ? null : x.Department.Name, x.IsActive)).ToListAsync(),
            memberships = await db.UserGroups.Select(x => new { x.UserId, x.GroupId }).ToListAsync() };
    }
    private static string Name(string value) { value = value.Trim(); if (value.Length is < 1 or > 100 || value.Any(char.IsControl) || value.Contains('/') || value.Contains('\\')) throw new ApiException(400, "Use a name of 1–100 characters without slashes."); return value; }
    public async Task CreateDepartment(string name)
    {
        await RequireAdmin(); name = Name(name);
        if (await db.Departments.AnyAsync(x => x.Name == name) || await db.Groups.AnyAsync(x => x.Name == name)) throw new ApiException(409, "This department/group name already exists.");
        var department = new Department { Name = name }; db.Departments.Add(department); db.Groups.Add(new() { Name = name, DepartmentId = department.Id });
        audit.Add(current.Id, "CREATE_DEPARTMENT", department.Id, "department", $"Created {name}"); await db.SaveChangesAsync();
    }
    public async Task CreateGroup(string name)
    {
        await RequireAdmin(); name = Name(name);
        if (await db.Groups.AnyAsync(x => x.Name == name)) throw new ApiException(409, "Group already exists.");
        var group = new Group { Name = name }; db.Groups.Add(group); audit.Add(current.Id, "CREATE_GROUP", group.Id, "group", $"Created {name}"); await db.SaveChangesAsync();
    }
    public async Task SetDepartment(Guid userId, Guid? departmentId)
    {
        await RequireAdmin(); var user = await db.Users.FindAsync(userId) ?? throw new ApiException(404, "User not found.");
        if (user.Email == StorageIdentity) throw new ApiException(400, "The storage identity cannot be assigned to a department.");
        if (departmentId != null && !await db.Departments.AnyAsync(x => x.Id == departmentId)) throw new ApiException(404, "Department not found.");
        if (user.DepartmentId == departmentId) return;
        var departmentGroups = db.Groups.Where(x => x.DepartmentId != null).Select(x => x.Id);
        db.UserGroups.RemoveRange(await db.UserGroups.Where(x => x.UserId == userId && departmentGroups.Contains(x.GroupId)).ToListAsync());
        user.DepartmentId = departmentId;
        if (departmentId != null) { var group = await db.Groups.SingleAsync(x => x.DepartmentId == departmentId); db.UserGroups.Add(new() { UserId = userId, GroupId = group.Id }); }
        audit.Add(current.Id, "CHANGE_DEPARTMENT", userId, "user", $"Changed department for {user.Email}"); await db.SaveChangesAsync();
    }
    public async Task CreateUser(string name, string email, string password, Guid? departmentId)
    {
        await RequireAdmin(); name = Name(name); email = email.Trim().ToLowerInvariant();
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || password.Length < 8) throw new ApiException(400, "Provide a valid email and a password of at least 8 characters.");
        if (await db.Users.AnyAsync(x => x.Email == email)) throw new ApiException(409, "Email already exists.");
        if (departmentId != null && !await db.Departments.AnyAsync(x => x.Id == departmentId)) throw new ApiException(404, "Department not found.");
        var role = await db.Roles.SingleAsync(x => x.Name == "Employee");
        var user = new User { Name = name, Email = email, RoleId = role.Id, DepartmentId = departmentId }; user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user);
        db.Folders.Add(new() { Name = "My Files", OwnerId = user.Id, IsRoot = true });
        foreach (var group in await db.Groups.Where(x => x.IsAllStaff || (departmentId != null && x.DepartmentId == departmentId)).ToListAsync()) db.UserGroups.Add(new() { UserId = user.Id, GroupId = group.Id });
        audit.Add(current.Id, "CREATE_USER", user.Id, "user", $"Created {email}"); await db.SaveChangesAsync();
    }
    public async Task SetActive(Guid userId, bool active)
    {
        await RequireAdmin(); if (userId == current.Id) throw new ApiException(400, "You cannot deactivate your own account.");
        var user = await db.Users.FindAsync(userId) ?? throw new ApiException(404, "User not found.");
        if (user.Email == StorageIdentity) throw new ApiException(400, "The storage identity cannot sign in.");
        user.IsActive = active; audit.Add(current.Id, "CHANGE_USER_STATUS", userId, "user", $"Set {user.Email} active={active}"); await db.SaveChangesAsync();
    }
    public async Task SetMember(Guid groupId, Guid userId, bool member)
    {
        await RequireAdmin(); var group = await db.Groups.FindAsync(groupId) ?? throw new ApiException(404, "Group not found.");
        if (group.DepartmentId != null || group.IsAllStaff || group.IsAdministrators) throw new ApiException(400, "This group is managed by department or role assignment.");
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.IsActive)) throw new ApiException(404, "Active user not found.");
        var row = await db.UserGroups.FindAsync(userId, groupId);
        if (member && row == null) db.UserGroups.Add(new() { UserId = userId, GroupId = groupId }); else if (!member && row != null) db.UserGroups.Remove(row);
        audit.Add(current.Id, "CHANGE_GROUP_MEMBERSHIP", groupId, "group", $"Set user {userId} member={member} of {group.Name}"); await db.SaveChangesAsync();
    }
    public async Task CreateTeam(string name, Guid groupId, Access access)
    {
        await RequireAdmin(); name = Name(name); Validate(access);
        if (await db.TeamSpaces.AnyAsync(x => x.Name == name)) throw new ApiException(409, "Team folder already exists.");
        if (!await db.Groups.AnyAsync(x => x.Id == groupId)) throw new ApiException(404, "Group not found.");
        var storage = await db.Users.SingleAsync(x => x.Email == StorageIdentity);
        var admins = await db.Groups.SingleAsync(x => x.IsAdministrators);
        var team = new TeamSpace { Name = name }; db.TeamSpaces.Add(team);
        var root = new Folder { Name = name, OwnerId = storage.Id, TeamSpaceId = team.Id, IsTeamRoot = true }; db.Folders.Add(root);
        db.GroupPermissions.Add(new() { GroupId = admins.Id, FolderId = root.Id, Access = Access.All });
        if (groupId != admins.Id) db.GroupPermissions.Add(new() { GroupId = groupId, FolderId = root.Id, Access = access });
        audit.Add(current.Id, "CREATE_TEAM_FOLDER", root.Id, "folder", $"Created team folder {name}"); await db.SaveChangesAsync();
    }
    public static void Validate(Access access) { if (((int)access & ~15) != 0 || (access != Access.None && (access & Access.Read) == 0)) throw new ApiException(400, "Permissions must include Read, or select No access."); }
    public async Task SetGroupShare(string type, Guid id, Guid groupId, Access access, bool remove = false)
    {
        await permissions.Require(current.Id, type, id, Access.Read | Access.Share); Validate(access);
        if ((access & await permissions.Get(current.Id, type, id)) != access) throw new ApiException(403, "You can only grant permissions you have.");
        if (!await db.Groups.AnyAsync(x => x.Id == groupId)) throw new ApiException(404, "Group not found.");
        var grant = await db.GroupPermissions.SingleOrDefaultAsync(x => x.GroupId == groupId && (type == "folder" ? x.FolderId == id : x.FileId == id));
        if (remove) { if (grant == null) throw new ApiException(400, "Remove inherited access at its source."); db.GroupPermissions.Remove(grant); }
        else if (grant != null) grant.Access = access;
        else db.GroupPermissions.Add(new() { GroupId = groupId, FolderId = type == "folder" ? id : null, FileId = type == "file" ? id : null, Access = access });
        audit.Add(current.Id, "CHANGE_GROUP_PERMISSION", id, type, $"{(remove ? "Removed" : "Set")} group {groupId} access {access}"); await db.SaveChangesAsync();
    }
}
