using System.Text;
using Atlas.Api.Entities;
using Atlas.Api.Services;
using Atlas.Api.Storage;
using Microsoft.EntityFrameworkCore;
namespace Atlas.Api.Data;
public class TeamDemoSeed(AppDbContext db, ObjectStorage storage)
{
    public async Task Initialize()
    {
        // Add this demo to existing databases once; never reset user-managed memberships on restart.
        if (await db.Groups.AnyAsync(x => x.IsAllStaff)) return;
        var employeeRole = await db.Roles.SingleAsync(x => x.Name == "Employee");
        var storageUser = new User { Email = OrganizationService.StorageIdentity, Name = "Team storage", IsActive = false, RoleId = employeeRole.Id };
        db.Users.Add(storageUser);
        var all = new Group { Name = "All Staff", IsAllStaff = true }; var admins = new Group { Name = "Workspace Administrators", IsAdministrators = true }; db.Groups.AddRange(all, admins);
        var departments = new[] { "Finance", "Operations", "Human Resources", "IT" }.Select(name => new Department { Name = name }).ToList(); db.Departments.AddRange(departments);
        var groups = departments.Select(x => new Group { Name = x.Name, DepartmentId = x.Id }).ToList(); db.Groups.AddRange(groups);
        foreach (var user in await db.Users.Include(x => x.Role).Where(x => x.IsActive).ToListAsync())
        {
            db.UserGroups.Add(new() { UserId = user.Id, GroupId = all.Id });
            if (user.Role.Name == "Admin") db.UserGroups.Add(new() { UserId = user.Id, GroupId = admins.Id });
            var name = user.Email switch { "finance@demo.local" => "Finance", "manager@demo.local" => "Operations", "employee@demo.local" => "Human Resources", _ => null };
            if (name != null) { var dept = departments.Single(x => x.Name == name); user.DepartmentId = dept.Id; db.UserGroups.Add(new() { UserId = user.Id, GroupId = groups.Single(x => x.DepartmentId == dept.Id).Id }); }
        }
        var createdKeys = new List<string>();
        try
        {
            foreach (var group in groups.Prepend(all))
            {
                var team = new TeamSpace { Name = group.IsAllStaff ? "All Staff" : group.Name }; db.TeamSpaces.Add(team);
                var root = new Folder { Name = team.Name, OwnerId = storageUser.Id, TeamSpaceId = team.Id, IsTeamRoot = true }; db.Folders.Add(root);
                db.GroupPermissions.Add(new() { GroupId = admins.Id, FolderId = root.Id, Access = Access.All });
                db.GroupPermissions.Add(new() { GroupId = group.Id, FolderId = root.Id, Access = group.IsAllStaff ? Access.Read : Access.All });
                var folder = new Folder { Name = group.IsAllStaff ? "Procedures" : "Internal documents", OwnerId = storageUser.Id, TeamSpaceId = team.Id, ParentFolderId = root.Id }; db.Folders.Add(folder);
                var text = group.IsAllStaff ? "# All Staff\n\nThese procedures are available to every active employee.\nDepartment-only content is available in the respective Team Folder.\n" : $"# {group.Name}\n\nInternal documents are available to this department.\nAdditional users or groups can be granted access through Share & manage access.\n";
                var bytes = Encoding.UTF8.GetBytes(text); var key = $"demo-teams/{Guid.NewGuid():N}"; createdKeys.Add(key);
                using var stream = new MemoryStream(bytes); await storage.Put(key, stream, bytes.Length, "text/markdown");
                db.Files.Add(new() { Name = "Department guide.md", OriginalName = "Department guide.md", OwnerId = storageUser.Id, TeamSpaceId = team.Id, ParentFolderId = folder.Id, ObjectKey = key, Size = bytes.Length, ContentType = "text/markdown" });
            }
            await db.SaveChangesAsync();
        }
        catch { foreach (var key in createdKeys) { try { await storage.Remove(key); } catch { } } throw; }
    }
}
