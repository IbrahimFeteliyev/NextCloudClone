using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;
public class FileVersionService(AppDbContext db, PermissionService permissions, ObjectStorage storage, AuditService audit)
{
    public static FileVersion Initial(Document file, Guid actor) => new()
    {
        FileId = file.Id, Number = file.Version, ObjectKey = file.ObjectKey, Size = file.Size,
        ContentType = file.ContentType, CreatedAt = file.CreatedAt, CreatedById = actor
    };
    // Caller commits the current content pointer and related audit/session state in one DB transaction.
    public async Task<string> Replace(Document file, Guid actor, int expectedVersion, Stream content, long size, string saveId, CancellationToken cancellationToken = default, bool allowEmpty = false)
    {
        await permissions.Require(actor, "file", file.Id, Access.Read | Access.Write);
        if (file.Version != expectedVersion) throw new ApiException(409, "The document changed outside this editing session. Reopen it before saving.");
        if (size < 0 || (!allowEmpty && size == 0) || size > 100 * 1024 * 1024) throw new ApiException(400, "Edited documents exceed the allowed size.");
        var key = $"documents/{Guid.NewGuid():N}";
        try { await storage.Put(key, content, size, file.ContentType, cancellationToken); cancellationToken.ThrowIfCancellationRequested(); }
        catch { try { await storage.Remove(key); } catch { /* Preserve the original storage failure. */ } throw; }
        file.Version++; file.ObjectKey = key; file.Size = size; file.UpdatedAt = DateTime.UtcNow;
        return key;
    }
    public async Task Snapshot(Guid actor, Guid fileId, DocumentLocks locks, CancellationToken cancellationToken)
    {
        using var lease = await locks.Enter(fileId, cancellationToken);
        await permissions.Require(actor, "file", fileId, Access.Read | Access.Write);
        var file = (await db.Files.FindAsync(fileId))!;
        await db.Entry(file).ReloadAsync(cancellationToken);
        await permissions.Require(actor, "file", fileId, Access.Read | Access.Write);
        if (!await db.FileVersions.AnyAsync(x => x.FileId == fileId && x.Number == file.Version, cancellationToken))
        {
            var snapshot = Initial(file, actor); snapshot.CreatedAt = DateTime.UtcNow;
            db.FileVersions.Add(snapshot);
            audit.Add(actor, "SAVE_FILE_VERSION", file.Id, "file", $"Manually saved {file.Name}, revision {file.Version}");
            await db.SaveChangesAsync(cancellationToken);
        }
    }
    public async Task RemoveUnused(string key)
    {
        if (await db.Files.IgnoreQueryFilters().AnyAsync(x => x.ObjectKey == key) || await db.FileVersions.AnyAsync(x => x.ObjectKey == key)
            || await db.OfficeSessions.AnyAsync(x => x.InitialObjectKey == key && x.ClosedAt == null && x.ExpiresAt > DateTime.UtcNow)) return;
        try { await storage.Remove(key); } catch { /* An orphaned object must not invalidate a committed save. */ }
    }
    public async Task Restore(Guid actor, Guid fileId, int number, int expectedVersion, DocumentLocks locks, CancellationToken cancellationToken)
    {
        using var lease = await locks.Enter(fileId, cancellationToken);
        await permissions.Require(actor, "file", fileId, Access.Read | Access.Write);
        var file = (await db.Files.FindAsync(fileId))!;
        await db.Entry(file).ReloadAsync(cancellationToken);
        if (db.Entry(file).State == EntityState.Detached) throw new ApiException(404, "File not found.");
        if (file.Version != expectedVersion) throw new ApiException(409, "The file changed. Refresh history before restoring.");
        if (number == file.Version) throw new ApiException(400, "This is already the current version.");
        if (await db.OfficeSessions.AnyAsync(x => x.FileId == fileId && x.ClosedAt == null && x.ExpiresAt > DateTime.UtcNow, cancellationToken))
            throw new ApiException(409, "Close the active ONLYOFFICE session before restoring a version.");
        var snapshot = await db.FileVersions.SingleOrDefaultAsync(x => x.FileId == fileId && x.Number == number, cancellationToken)
            ?? throw new ApiException(404, "File version not found.");
        using var content = await storage.Get(snapshot.ObjectKey);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string? newKey = null;
        var oldKey = file.ObjectKey;
        try
        {
            file.ContentType = snapshot.ContentType;
            newKey = await Replace(file, actor, expectedVersion, content, content.Length, $"restore:{Guid.NewGuid():N}", cancellationToken, allowEmpty: true);
            audit.Add(actor, "RESTORE_FILE_VERSION", file.Id, "file", $"Restored {file.Name} from version {number} as version {file.Version}");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (newKey != null) { try { await storage.Remove(newKey); } catch { /* Preserve the original failure. */ } }
            if (ex is DbUpdateConcurrencyException) throw new ApiException(409, "The file changed. Refresh history before restoring.");
            throw;
        }
        await RemoveUnused(oldKey);
    }
    public async Task DeleteSnapshot(Guid actor, Guid fileId, int number, DocumentLocks locks, CancellationToken cancellationToken)
    {
        using var lease = await locks.Enter(fileId, cancellationToken);
        await permissions.Require(actor, "file", fileId, Access.Read | Access.Delete);
        var snapshot = await db.FileVersions.SingleOrDefaultAsync(x => x.FileId == fileId && x.Number == number, cancellationToken)
            ?? throw new ApiException(404, "File version not found.");
        var key = snapshot.ObjectKey;
        db.FileVersions.Remove(snapshot);
        audit.Add(actor, "DELETE_FILE_VERSION", fileId, "file", $"Deleted saved version {number}");
        await db.SaveChangesAsync(cancellationToken);
        await RemoveUnused(key);
    }
    public async Task<object> List(Guid actor, Guid fileId)
    {
        await permissions.Require(actor, "file", fileId, Access.Read);
        var file = (await db.Files.FindAsync(fileId))!;
        return await db.FileVersions.Where(x => x.FileId == fileId).OrderByDescending(x => x.Number)
            .Select(x => new { x.Number, x.Size, x.ContentType, x.CreatedAt, createdBy = x.CreatedBy.Name, isCurrent = x.ObjectKey == file.ObjectKey, currentVersion = file.Version }).ToListAsync();
    }
    public async Task<(MemoryStream Stream, string ContentType, string Name)> Download(Guid actor, Guid fileId, int version)
    {
        await permissions.Require(actor, "file", fileId, Access.Read);
        var snapshot = await db.FileVersions.SingleOrDefaultAsync(x => x.FileId == fileId && x.Number == version)
            ?? throw new ApiException(404, "File version not found.");
        var file = (await db.Files.FindAsync(fileId))!;
        return (await storage.Get(snapshot.ObjectKey), snapshot.ContentType, file.Name);
    }
}
