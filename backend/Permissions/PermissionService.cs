using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Atlas.Api.Permissions;

// Nearest explicit grant replaces inherited access. An explicit None denies inheritance.
// Roles do not bypass resource permissions; Admin only grants access to all audit events.
public class PermissionService(AppDbContext db)
{
    private async Task<Access?> GroupAccess(Guid userId, Guid id, bool file)
    {
        var groups = db.UserGroups.Where(x => x.UserId == userId).Select(x => x.GroupId);
        var grants = await db.GroupPermissions.Where(x => groups.Contains(x.GroupId) && (file ? x.FileId == id : x.FolderId == id)).Select(x => x.Access).ToListAsync();
        if (grants.Count == 0) return null;
        return grants.Contains(Access.None) ? Access.None : grants.Aggregate(Access.None, (a, b) => a | b);
    }
    public async Task<Access> FolderAccess(Guid userId, Guid folderId)
    {
        if ((await db.Users.FindAsync(userId))?.IsActive != true) return Access.None;
        var visited = new HashSet<Guid>();
        Guid? cursor = folderId;
        while (cursor is Guid id && visited.Add(id))
        {
            var folder = await db.Folders.FindAsync(id) ?? throw new ApiException(404, "Folder not found.");
            if (folder.DeletedAt != null) throw new ApiException(404, "Folder is in Trash.");
            if (folder.TeamSpaceId == null && folder.OwnerId == userId) return Access.All;
            var grant = await db.FolderPermissions.SingleOrDefaultAsync(x => x.FolderId == id && x.UserId == userId);
            if (grant != null) return grant.Access;
            var group = await GroupAccess(userId, id, false);
            if (group != null) return group.Value;
            cursor = folder.ParentFolderId;
        }
        return Access.None;
    }
    public async Task<Access> FileAccess(Guid userId, Guid fileId)
    {
        if ((await db.Users.FindAsync(userId))?.IsActive != true) return Access.None;
        var file = await db.Files.FindAsync(fileId) ?? throw new ApiException(404, "File not found.");
        if (file.DeletedAt != null) throw new ApiException(404, "File is in Trash.");
        if (file.TeamSpaceId == null && file.OwnerId == userId) return Access.All;
        var grant = await db.FilePermissions.SingleOrDefaultAsync(x => x.FileId == fileId && x.UserId == userId);
        return grant?.Access ?? await GroupAccess(userId, fileId, true) ?? await FolderAccess(userId, file.ParentFolderId);
    }
    public Task<Access> Get(Guid userId, string type, Guid id) => type switch
    {
        "folder" => FolderAccess(userId, id), "file" => FileAccess(userId, id),
        _ => throw new ApiException(400, "Resource type must be folder or file.")
    };
    public async Task Require(Guid userId, string type, Guid id, Access required)
    {
        if ((await Get(userId, type, id) & required) != required)
            throw new ApiException(403, $"You don't have {required.ToString().ToUpperInvariant()} permission for this resource.");
    }
    public async Task<List<DTOs.ShareDto>> Shares(string type, Guid id)
    {
        // Resolve each recipient independently, so a child override is reflected in the access list.
        var users = await db.Users.Where(x => x.IsActive).ToListAsync();
        var result = new List<DTOs.ShareDto>();
        var file = type == "file" ? await db.Files.FindAsync(id) : null;
        var folder = await db.Folders.FindAsync(file?.ParentFolderId ?? id);
        var owner = file?.OwnerId ?? folder!.OwnerId;
        foreach (var user in users.Where(x => x.Id != owner))
        {
            if (file != null)
            {
                var direct = await db.FilePermissions.SingleOrDefaultAsync(x => x.FileId == id && x.UserId == user.Id);
                if (direct != null) { result.Add(new(user.Id, user.Name, user.Email, direct.Access, false, file.Name)); continue; }
                var group = await GroupAccess(user.Id, file.Id, true);
                if (group != null) { result.Add(new(user.Id, user.Name, user.Email, group.Value, true, "Group membership")); continue; }
            }
            var cursor = folder;
            while (cursor != null)
            {
                var grant = await db.FolderPermissions.SingleOrDefaultAsync(x => x.FolderId == cursor.Id && x.UserId == user.Id);
                if (grant != null)
                {
                    result.Add(new(user.Id, user.Name, user.Email, grant.Access, type == "file" || cursor.Id != id, cursor.Name));
                    break;
                }
                var group = await GroupAccess(user.Id, cursor.Id, false);
                if (group != null) { result.Add(new(user.Id, user.Name, user.Email, group.Value, true, $"Group membership · {cursor.Name}")); break; }
                cursor = cursor.ParentFolderId is Guid parent ? await db.Folders.FindAsync(parent) : null;
            }
            if (!result.Any(x => x.UserId == user.Id))
            {
                var effective = await Get(user.Id, type, id);
                if (effective != Access.None) result.Add(new(user.Id, user.Name, user.Email, effective, true, "Group membership"));
            }
        }
        return result;
    }
    public async Task<List<DTOs.GroupShareDto>> GroupShares(string type, Guid id)
    {
        var result = new List<DTOs.GroupShareDto>();
        var file = type == "file" ? await db.Files.FindAsync(id) : null;
        var cursor = await db.Folders.FindAsync(file?.ParentFolderId ?? id);
        if (file != null) foreach (var grant in await db.GroupPermissions.Include(x => x.Group).Where(x => x.FileId == id).ToListAsync())
            result.Add(new(grant.GroupId, grant.Group.Name, grant.Access, false, file.Name));
        while (cursor != null)
        {
            foreach (var grant in await db.GroupPermissions.Include(x => x.Group).Where(x => x.FolderId == cursor.Id).ToListAsync())
                if (!result.Any(x => x.GroupId == grant.GroupId)) result.Add(new(grant.GroupId, grant.Group.Name, grant.Access, file != null || cursor.Id != id, cursor.Name));
            cursor = cursor.ParentFolderId is Guid parent ? await db.Folders.FindAsync(parent) : null;
        }
        return result;
    }
}
