using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Services;
using Microsoft.EntityFrameworkCore;
namespace Atlas.Api.Permissions;

// Nearest explicit grant replaces inherited access. An explicit None denies inheritance.
// Roles do not bypass resource permissions; Admin only grants access to all audit events.
public class PermissionService(AppDbContext db)
{
    public async Task<Access> FolderAccess(Guid userId, Guid folderId)
    {
        var visited = new HashSet<Guid>();
        Guid? cursor = folderId;
        while (cursor is Guid id && visited.Add(id))
        {
            var folder = await db.Folders.FindAsync(id) ?? throw new ApiException(404, "Folder not found.");
            if (folder.OwnerId == userId) return Access.All;
            var grant = await db.FolderPermissions.SingleOrDefaultAsync(x => x.FolderId == id && x.UserId == userId);
            if (grant != null) return grant.Access;
            cursor = folder.ParentFolderId;
        }
        return Access.None;
    }
    public async Task<Access> FileAccess(Guid userId, Guid fileId)
    {
        var file = await db.Files.FindAsync(fileId) ?? throw new ApiException(404, "File not found.");
        if (file.OwnerId == userId) return Access.All;
        var grant = await db.FilePermissions.SingleOrDefaultAsync(x => x.FileId == fileId && x.UserId == userId);
        return grant?.Access ?? await FolderAccess(userId, file.ParentFolderId);
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
        var users = await db.Users.ToListAsync();
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
                cursor = cursor.ParentFolderId is Guid parent ? await db.Folders.FindAsync(parent) : null;
            }
        }
        return result;
    }
}
