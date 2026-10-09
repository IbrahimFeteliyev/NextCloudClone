using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Storage;
using Microsoft.EntityFrameworkCore;
namespace Atlas.Api.Services;

public class DocumentService(AppDbContext db, CurrentUser user, PermissionService permissions, ObjectStorage storage, AuditService audit, ILogger<DocumentService>? logger = null, FileVersionService? versionService = null, DocumentLocks? documentLocks = null)
{
    private static string CleanName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 255 || name is "." or ".." || name.IndexOfAny(['/', '\\', '\0']) >= 0 || name.Any(char.IsControl))
            throw new ApiException(400, "Use a name of 1–255 characters without slashes or control characters.");
        return name;
    }
    private async Task UniqueName(Guid parent, string name, Guid? except = null)
    {
        if (await db.Folders.AnyAsync(x => x.ParentFolderId == parent && x.Name == name && x.Id != except) ||
            await db.Files.AnyAsync(x => x.ParentFolderId == parent && x.Name == name && x.Id != except))
            throw new ApiException(409, "A file or folder with this name already exists here.");
    }
    private async Task<ResourceDto> ToDto(Folder f) => new(f.Id, f.Name, "folder", f.OwnerId,
        f.Owner.Name, f.Owner.Email, f.UpdatedAt, 0, f.ParentFolderId, await permissions.FolderAccess(user.Id, f.Id), await permissions.Shares("folder", f.Id), IsFavorite: await db.Favorites.AnyAsync(x => x.UserId == user.Id && x.FolderId == f.Id));
    private async Task<ResourceDto> ToDto(Document f) => new(f.Id, f.Name, "file", f.OwnerId,
        f.Owner.Name, f.Owner.Email, f.UpdatedAt, f.Size, f.ParentFolderId, await permissions.FileAccess(user.Id, f.Id), await permissions.Shares("file", f.Id), FileContentTypes.Resolve(f), await db.Favorites.AnyAsync(x => x.UserId == user.Id && x.FileId == f.Id));
    public async Task<ExplorerDto> Explore(string view, Guid? folderId, string? search, int? page = null, int pageSize = 20)
    {
        if (view is not ("files" or "shared" or "recent" or "favorites" or "shared-by-me")) throw new ApiException(400, "Unknown view.");
        var folders = await db.Folders.Include(x => x.Owner).ToListAsync(); var files = await db.Files.Include(x => x.Owner).ToListAsync();
        var favorites = await db.Favorites.Where(x => x.UserId == user.Id).ToListAsync();
        var sharedFolders = await db.FolderPermissions.Where(x => x.Access != Access.None && x.UserId != user.Id).Select(x => x.FolderId).ToListAsync();
        var sharedFiles = await db.FilePermissions.Where(x => x.Access != Access.None && x.UserId != user.Id).Select(x => x.FileId).ToListAsync();
        var crumbs = new List<BreadcrumbDto>(); Access access = Access.None; bool searching = !string.IsNullOrWhiteSpace(search);
        if (folderId == null && view == "files" && !searching) folderId = folders.Single(x => x.OwnerId == user.Id && x.IsRoot).Id;
        if (folderId is Guid id)
        {
            await permissions.Require(user.Id, "folder", id, Access.Read); access = await permissions.FolderAccess(user.Id, id);
            var cursor = folders.Single(x => x.Id == id);
            while (cursor != null && (await permissions.FolderAccess(user.Id, cursor.Id) & Access.Read) != 0)
            { crumbs.Insert(0, new(cursor.Id, cursor.Name)); cursor = folders.SingleOrDefault(x => x.Id == cursor.ParentFolderId); }
        }
        var items = new List<ResourceDto>();
        foreach (var f in folders.Where(x => !x.IsRoot))
        {
            if (folderId == null && view == "favorites" && !favorites.Any(x => x.FolderId == f.Id)) continue;
            if (folderId == null && view == "shared-by-me" && (f.OwnerId != user.Id || !sharedFolders.Contains(f.Id))) continue;
            if (view == "recent" && !searching && folderId == null) continue;
            if (folderId != null && !searching && f.ParentFolderId != folderId) continue;
            if (folderId == null && !searching && view == "shared")
            {
                if (f.OwnerId == user.Id || !await db.FolderPermissions.AnyAsync(x => x.FolderId == f.Id && x.UserId == user.Id)) continue;
                if (f.ParentFolderId is Guid parent && (await permissions.FolderAccess(user.Id, parent) & Access.Read) != 0) continue;
            }
            if (searching && !f.Name.Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if ((await permissions.FolderAccess(user.Id, f.Id) & Access.Read) != 0) items.Add(await ToDto(f));
        }
        foreach (var f in files)
        {
            if (folderId == null && view == "favorites" && !favorites.Any(x => x.FileId == f.Id)) continue;
            if (folderId == null && view == "shared-by-me" && (f.OwnerId != user.Id || !sharedFiles.Contains(f.Id))) continue;
            if (folderId != null && !searching && f.ParentFolderId != folderId) continue;
            if (folderId == null && !searching && view == "shared")
            {
                if (f.OwnerId == user.Id || !await db.FilePermissions.AnyAsync(x => x.FileId == f.Id && x.UserId == user.Id)) continue;
                if ((await permissions.FolderAccess(user.Id, f.ParentFolderId) & Access.Read) != 0) continue;
            }
            if (searching && !f.Name.Contains(search!.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if ((await permissions.FileAccess(user.Id, f.Id) & Access.Read) != 0) items.Add(await ToDto(f));
        }
        var sorted = view == "recent" && folderId == null ? items.OrderByDescending(x => x.Modified).ThenBy(x => x.Id).ToList()
            : items.OrderBy(x => x.Type == "folder" ? 0 : 1).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var total = sorted.Count; var currentPage = Math.Max(1, page ?? 1); pageSize = Math.Clamp(pageSize, 1, 100);
        if (view == "recent" && page != null) sorted = sorted.Skip((currentPage - 1) * pageSize).Take(pageSize).ToList();
        return new(folderId, access, crumbs, sorted, total, currentPage, pageSize);
    }
    public async Task SetFavorite(string type, Guid id, bool favorite)
    {
        await permissions.Require(user.Id, type, id, Access.Read);
        if (type == "folder" && (await db.Folders.FindAsync(id))!.IsRoot) throw new ApiException(400, "Choose a file or a regular folder to favorite.");
        var entry = await db.Favorites.SingleOrDefaultAsync(x => x.UserId == user.Id && (type == "folder" ? x.FolderId == id : x.FileId == id));
        if (favorite && entry == null) db.Favorites.Add(new() { UserId = user.Id, FolderId = type == "folder" ? id : null, FileId = type == "file" ? id : null });
        else if (!favorite && entry != null) db.Favorites.Remove(entry);
        await db.SaveChangesAsync();
    }
    public async Task<ResourceDto> CreateFolder(CreateFolderRequest request)
    {
        await permissions.Require(user.Id, "folder", request.ParentFolderId, Access.Read | Access.Write); var parent = (await db.Folders.FindAsync(request.ParentFolderId))!;
        var name = CleanName(request.Name); await UniqueName(parent.Id, name);
        var folder = new Folder { Name = name, OwnerId = parent.OwnerId, ParentFolderId = parent.Id };
        db.Folders.Add(folder); parent.UpdatedAt = DateTime.UtcNow; audit.Add(user.Id, "CREATE_FOLDER", folder.Id, "folder", $"Created {name} in {parent.Name}");
        await db.SaveChangesAsync(); await db.Entry(folder).Reference(x => x.Owner).LoadAsync(); return await ToDto(folder);
    }
    public async Task<ResourceDto> Upload(Guid parentId, IFormFile upload, CancellationToken cancellationToken = default, bool replace = false, Guid? replaceFileId = null, int? expectedVersion = null)
    {
        await permissions.Require(user.Id, "folder", parentId, Access.Read | Access.Write);
        if (upload.Length > 100 * 1024 * 1024) throw new ApiException(400, "Files must be 100 MB or smaller.");
        var parent = (await db.Folders.FindAsync(parentId))!; var name = CleanName(Path.GetFileName(upload.FileName.Replace('\\', '/')));
        if (await db.Folders.AnyAsync(x => x.ParentFolderId == parentId && x.Name == name)) throw new ApiException(409, "A folder has this name. Choose another file name; folders cannot be replaced by uploads.");
        var existing = await db.Files.SingleOrDefaultAsync(x => x.ParentFolderId == parentId && x.Name == name);
        if (existing != null)
        {
            await permissions.Require(user.Id, "file", existing.Id, Access.Read | Access.Write);
            if (!replace) throw new ApiException(409, "A file with this name exists. Replace it or cancel the upload.", new { fileId = existing.Id, version = existing.Version, canReplace = true });
            if (replaceFileId != existing.Id || expectedVersion == null) throw new ApiException(409, "The destination changed. Retry the upload to confirm replacement.");
            using var lease = await (documentLocks ?? new DocumentLocks()).Enter(existing.Id, cancellationToken);
            await db.Entry(existing).ReloadAsync(cancellationToken);
            await permissions.Require(user.Id, "file", existing.Id, Access.Read | Access.Write);
            if (existing.ParentFolderId != parentId || existing.Name != name || existing.Version != expectedVersion) throw new ApiException(409, "The destination changed. Retry the upload to confirm replacement.");
            if (await db.OfficeSessions.AnyAsync(x => x.FileId == existing.Id && x.ClosedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken)) throw new ApiException(409, "Close the active ONLYOFFICE session before replacing this file.");
            var versions = versionService ?? new FileVersionService(db, permissions, storage, audit);
            var oldKey = existing.ObjectKey; string? newKey = null;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                existing.ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType;
                using var stream = upload.OpenReadStream();
                newKey = await versions.Replace(existing, user.Id, expectedVersion.Value, stream, upload.Length, $"upload:{Guid.NewGuid():N}", cancellationToken, allowEmpty: true);
                await permissions.Require(user.Id, "folder", parentId, Access.Read | Access.Write);
                parent.UpdatedAt = DateTime.UtcNow;
                audit.Add(user.Id, "REPLACE_FILE", existing.Id, "file", $"Replaced content of {name}");
                await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                if (newKey != null) { try { await storage.Remove(newKey); } catch { /* Preserve original failure. */ } }
                if (ex is DbUpdateConcurrencyException) throw new ApiException(409, "The destination changed. Retry the upload.");
                throw;
            }
            await versions.RemoveUnused(oldKey);
            await db.Entry(existing).Reference(x => x.Owner).LoadAsync(); return await ToDto(existing);
        }
        if (replace) throw new ApiException(409, "The destination no longer exists. Retry as a new upload.");
        await UniqueName(parentId, name);
        var doc = new Document { Name = name, OriginalName = name, ObjectKey = $"documents/{Guid.NewGuid():N}", Size = upload.Length,
            ContentType = string.IsNullOrWhiteSpace(upload.ContentType) ? "application/octet-stream" : upload.ContentType, OwnerId = parent.OwnerId, ParentFolderId = parent.Id };
        try
        {
            using var stream = upload.OpenReadStream();
            await storage.Put(doc.ObjectKey, stream, doc.Size, doc.ContentType, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await permissions.Require(user.Id, "folder", parentId, Access.Read | Access.Write);
            db.Files.Add(doc); parent.UpdatedAt = DateTime.UtcNow; audit.Add(user.Id, "UPLOAD_FILE", doc.Id, "file", $"Uploaded {name} to {parent.Name}");
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try { await storage.Remove(doc.ObjectKey); } catch (Exception ex) { logger?.LogWarning(ex, "Could not remove incomplete upload {ObjectKey}", doc.ObjectKey); }
            throw;
        }
        await db.Entry(doc).Reference(x => x.Owner).LoadAsync(); return await ToDto(doc);
    }
    public async Task<(MemoryStream Stream, string ContentType, string Name)> Download(Guid id)
    {
        await permissions.Require(user.Id, "file", id, Access.Read); var f = (await db.Files.FindAsync(id))!; var stream = await storage.Get(f.ObjectKey);
        audit.Add(user.Id, "DOWNLOAD_FILE", id, "file", $"Downloaded {f.Name}"); try { await db.SaveChangesAsync(); } catch { stream.Dispose(); throw; }
        return (stream, f.ContentType, f.Name);
    }
    public async Task Rename(string type, Guid id, string name)
    {
        await permissions.Require(user.Id, type, id, Access.Read | Access.Write); name = CleanName(name);
        if (type == "folder")
        {
            var f = (await db.Folders.FindAsync(id))!; if (f.IsRoot) throw new ApiException(400, "Your personal root cannot be renamed.");
            await UniqueName(f.ParentFolderId!.Value, name, id); audit.Add(user.Id, "RENAME_FOLDER", id, type, $"Renamed {f.Name} to {name}"); f.Name = name; f.UpdatedAt = DateTime.UtcNow;
        }
        else { var f = (await db.Files.FindAsync(id))!; await UniqueName(f.ParentFolderId, name, id); audit.Add(user.Id, "RENAME_FILE", id, type, $"Renamed {f.Name} to {name}"); f.Name = name; f.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
    }
    public async Task Move(string type, Guid id, Guid destination)
    {
        await permissions.Require(user.Id, type, id, Access.Read | Access.Write); await permissions.Require(user.Id, "folder", destination, Access.Read | Access.Write);
        var target = (await db.Folders.FindAsync(destination))!;
        if (type == "folder")
        {
            var f = (await db.Folders.FindAsync(id))!; if (f.IsRoot) throw new ApiException(400, "Your personal root cannot be moved.");
            if (f.OwnerId != user.Id || target.OwnerId != f.OwnerId) throw new ApiException(403, "Only the owner can move resources, within their own workspace.");
            var cursor = target; while (cursor != null) { if (cursor.Id == id) throw new ApiException(400, "A folder cannot be moved into itself or a subfolder."); cursor = cursor.ParentFolderId is Guid p ? await db.Folders.FindAsync(p) : null; }
            await UniqueName(destination, f.Name, id); f.ParentFolderId = destination; f.UpdatedAt = DateTime.UtcNow; audit.Add(user.Id, "MOVE_FOLDER", id, type, $"Moved {f.Name} to {target.Name}");
        }
        else
        {
            var f = (await db.Files.FindAsync(id))!; if (f.OwnerId != user.Id || target.OwnerId != f.OwnerId) throw new ApiException(403, "Only the owner can move resources, within their own workspace.");
            await UniqueName(destination, f.Name, id); f.ParentFolderId = destination; f.UpdatedAt = DateTime.UtcNow; audit.Add(user.Id, "MOVE_FILE", id, type, $"Moved {f.Name} to {target.Name}");
        }
        target.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync();
    }
    public async Task Delete(string type, Guid id)
    {
        await permissions.Require(user.Id, type, id, Access.Read | Access.Delete); var files = new List<Document>(); var folders = new List<Folder>();
        if (type == "file") files.Add((await db.Files.FindAsync(id))!);
        else
        {
            var root = (await db.Folders.FindAsync(id))!; if (root.IsRoot) throw new ApiException(400, "Your personal root cannot be deleted.");
            var all = await db.Folders.ToListAsync(); var pending = new Queue<Folder>(); pending.Enqueue(root);
            while (pending.TryDequeue(out var f)) { await permissions.Require(user.Id, "folder", f.Id, Access.Read | Access.Delete); folders.Add(f); foreach (var c in all.Where(x => x.ParentFolderId == f.Id)) pending.Enqueue(c); }
            var ids = folders.Select(x => x.Id).ToList(); files = await db.Files.Where(x => ids.Contains(x.ParentFolderId)).ToListAsync();
        }
        foreach (var f in files) await permissions.Require(user.Id, "file", f.Id, Access.Read | Access.Delete);
        var deletedAt = DateTime.UtcNow;
        foreach (var f in files) { f.DeletedAt = deletedAt; f.TrashRootId = id; audit.Add(user.Id, "DELETE_FILE", f.Id, "file", $"Moved {f.Name} to Trash"); }
        foreach (var f in folders) { f.DeletedAt = deletedAt; f.TrashRootId = id; audit.Add(user.Id, "DELETE_FOLDER", f.Id, "folder", $"Moved {f.Name} to Trash"); }
        var idsToClose = files.Select(x => x.Id).ToList();
        foreach (var session in await db.OfficeSessions.Where(x => idsToClose.Contains(x.FileId) && x.ClosedAt == null).ToListAsync()) session.ClosedAt = deletedAt;
        await db.SaveChangesAsync();
    }
    public async Task<object> Trash()
    {
        var folders = await db.Folders.IgnoreQueryFilters().Where(x => x.OwnerId == user.Id && x.DeletedAt != null && x.TrashRootId == x.Id).ToListAsync();
        var files = await db.Files.IgnoreQueryFilters().Where(x => x.OwnerId == user.Id && x.DeletedAt != null && x.TrashRootId == x.Id).ToListAsync();
        return folders.Select(x => new { x.Id, x.Name, type = "folder", size = 0L, deletedAt = x.DeletedAt!.Value, retainUntil = x.DeletedAt.Value.AddDays(30) })
            .Concat(files.Select(x => new { x.Id, x.Name, type = "file", size = x.Size, deletedAt = x.DeletedAt!.Value, retainUntil = x.DeletedAt.Value.AddDays(30) }))
            .OrderByDescending(x => x.deletedAt).ToList();
    }
    public async Task RestoreTrash(string type, Guid id)
    {
        if (type is not ("file" or "folder")) throw new ApiException(400, "Unknown resource type.");
        var folders = await db.Folders.IgnoreQueryFilters().Where(x => x.TrashRootId == id && x.DeletedAt != null).ToListAsync();
        var files = await db.Files.IgnoreQueryFilters().Where(x => x.TrashRootId == id && x.DeletedAt != null).ToListAsync();
        var folder = folders.SingleOrDefault(x => x.Id == id);
        var file = files.SingleOrDefault(x => x.Id == id);
        if (type == "folder" ? folder == null : file == null) throw new ApiException(404, "Trash item not found.");
        if (folders.Any(x => x.OwnerId != user.Id) || files.Any(x => x.OwnerId != user.Id)) throw new ApiException(403, "Only the owner can restore items from Trash.");
        var parentId = folder?.ParentFolderId ?? file!.ParentFolderId;
        await permissions.Require(user.Id, "folder", parentId, Access.Read | Access.Write);
        await UniqueName(parentId, folder?.Name ?? file!.Name, id);
        foreach (var f in folders)
            if (await db.Folders.AnyAsync(x => x.ParentFolderId == f.ParentFolderId && x.Name == f.Name) || await db.Files.AnyAsync(x => x.ParentFolderId == f.ParentFolderId && x.Name == f.Name))
                throw new ApiException(409, "A restored folder name conflicts with an existing item.");
        foreach (var f in files)
            if (await db.Files.AnyAsync(x => x.ParentFolderId == f.ParentFolderId && x.Name == f.Name) || await db.Folders.AnyAsync(x => x.ParentFolderId == f.ParentFolderId && x.Name == f.Name))
                throw new ApiException(409, "A restored file name conflicts with an existing item.");
        foreach (var f in folders) { f.DeletedAt = null; f.TrashRootId = null; }
        foreach (var f in files) { f.DeletedAt = null; f.TrashRootId = null; }
        audit.Add(user.Id, "RESTORE_TRASH", id, type, $"Restored {folder?.Name ?? file!.Name} from Trash");
        await db.SaveChangesAsync();
    }
    public async Task<List<ShareDto>> GetShares(string type, Guid id) { await permissions.Require(user.Id, type, id, Access.Read); return await permissions.Shares(type, id); }
    public async Task SetShare(string type, Guid id, ShareRequest request)
    {
        await permissions.Require(user.Id, type, id, Access.Read | Access.Share);
        if (((int)request.Permissions & ~15) != 0 || (request.Permissions != Access.None && (request.Permissions & Access.Read) == 0)) throw new ApiException(400, "Grants must include READ. Choose no permissions to block inherited access.");
        if ((request.Permissions & await permissions.Get(user.Id, type, id)) != request.Permissions) throw new ApiException(403, "You can only grant permissions you have yourself.");
        if (!await db.Users.AnyAsync(x => x.Id == request.UserId)) throw new ApiException(404, "User not found.");
        var owner = type == "folder" ? (await db.Folders.FindAsync(id))!.OwnerId : (await db.Files.FindAsync(id))!.OwnerId;
        if (request.UserId == owner || request.UserId == user.Id) throw new ApiException(400, "You cannot change your own or the owner's access.");
        bool changed;
        if (type == "folder")
        {
            var g = await db.FolderPermissions.SingleOrDefaultAsync(x => x.FolderId == id && x.UserId == request.UserId); changed = g != null;
            if (g == null) db.FolderPermissions.Add(new() { FolderId = id, UserId = request.UserId, Access = request.Permissions }); else g.Access = request.Permissions;
            (await db.Folders.FindAsync(id))!.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var g = await db.FilePermissions.SingleOrDefaultAsync(x => x.FileId == id && x.UserId == request.UserId); changed = g != null;
            if (g == null) db.FilePermissions.Add(new() { FileId = id, UserId = request.UserId, Access = request.Permissions }); else g.Access = request.Permissions;
            (await db.Files.FindAsync(id))!.UpdatedAt = DateTime.UtcNow;
        }
        var recipient = (await db.Users.FindAsync(request.UserId))!;
        audit.Add(user.Id, changed ? "CHANGE_PERMISSION" : type == "folder" ? "SHARE_FOLDER" : "SHARE_FILE", id, type, $"Set {recipient.Email} access to {request.Permissions}"); await db.SaveChangesAsync();
    }
    public async Task RemoveShare(string type, Guid id, Guid recipient)
    {
        await permissions.Require(user.Id, type, id, Access.Read | Access.Share); if (recipient == user.Id) throw new ApiException(400, "You cannot change your own access.");
        if (type == "folder") { var g = await db.FolderPermissions.SingleOrDefaultAsync(x => x.FolderId == id && x.UserId == recipient); if (g == null) throw new ApiException(400, "Change inherited access at its source, or override it here."); db.FolderPermissions.Remove(g); }
        else { var g = await db.FilePermissions.SingleOrDefaultAsync(x => x.FileId == id && x.UserId == recipient); if (g == null) throw new ApiException(400, "Change inherited access at its source, or override it here."); db.FilePermissions.Remove(g); }
        audit.Add(user.Id, "CHANGE_PERMISSION", id, type, $"Removed direct grant for {recipient}; parent permissions now apply"); await db.SaveChangesAsync();
    }
    public async Task<object> Stats() => new { usedBytes = await db.Files.Where(x => x.OwnerId == user.Id).SumAsync(x => x.Size), fileCount = await db.Files.CountAsync(x => x.OwnerId == user.Id), folderCount = await db.Folders.CountAsync(x => x.OwnerId == user.Id && !x.IsRoot) };
    public async Task<object> Audit()
    {
        var logs = await db.AuditLogs.Include(x => x.User).OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id).ToListAsync(); var visible = new List<AuditLog>();
        foreach (var log in logs)
        {
            if (user.IsAdmin || log.UserId == user.Id) { visible.Add(log); continue; }
            if (log.ResourceId is Guid resource && log.ResourceType is "file" or "folder") { try { if ((await permissions.Get(user.Id, log.ResourceType, resource) & Access.Read) != 0) visible.Add(log); } catch (ApiException e) when (e.Status == 404) { } }
        }
        return visible.Select(x => new { x.Id, user = x.User.Name, email = x.User.Email, x.Action, x.ResourceId, x.ResourceType, x.Timestamp, x.Details });
    }
}
