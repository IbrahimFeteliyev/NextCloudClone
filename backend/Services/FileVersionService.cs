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
    // Caller commits this snapshot, the file pointer, and the editor session in one DB transaction.
    public async Task<string> Replace(Document file, Guid actor, int expectedVersion, Stream content, long size, string saveId)
    {
        await permissions.Require(actor, "file", file.Id, Access.Read | Access.Write);
        if (file.Version != expectedVersion) throw new ApiException(409, "The document changed outside this editing session. Reopen it before saving.");
        if (size is <= 0 or > 100 * 1024 * 1024) throw new ApiException(400, "Edited documents must be between 1 byte and 100 MB.");
        var key = $"documents/{Guid.NewGuid():N}";
        await storage.Put(key, content, size, file.ContentType);
        file.Version++; file.ObjectKey = key; file.Size = size; file.UpdatedAt = DateTime.UtcNow;
        db.FileVersions.Add(new() { FileId = file.Id, Number = file.Version, ObjectKey = key, Size = size,
            ContentType = file.ContentType, CreatedById = actor, SaveId = saveId });
        audit.Add(actor, "SAVE_FILE_VERSION", file.Id, "file", $"Saved {file.Name}, version {file.Version} from ONLYOFFICE");
        return key;
    }
    public async Task<object> List(Guid actor, Guid fileId)
    {
        await permissions.Require(actor, "file", fileId, Access.Read);
        return await db.FileVersions.Where(x => x.FileId == fileId).OrderByDescending(x => x.Number)
            .Select(x => new { x.Number, x.Size, x.ContentType, x.CreatedAt, createdBy = x.CreatedBy.Name }).ToListAsync();
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
