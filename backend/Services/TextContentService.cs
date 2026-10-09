using System.Text;
using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;

public class TextContentService(AppDbContext db, PermissionService permissions, FileVersionService versions,
    DocumentLocks locks, ObjectStorage storage, AuditService audit, ILogger<TextContentService> logger)
{
    public const int MaxBytes = 512 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public async Task Save(Guid actor, Guid id, string content, int expectedVersion, CancellationToken cancellationToken)
    {
        await permissions.Require(actor, "file", id, Access.Read | Access.Write);
        using var lease = await locks.Enter(id, cancellationToken);
        var file = (await db.Files.FindAsync([id], cancellationToken))!;
        await db.Entry(file).ReloadAsync(cancellationToken);
        if (db.Entry(file).State == EntityState.Detached) throw new ApiException(404, "File not found.");
        if (TextFileTypes.Resolve(file.Name, FileContentTypes.Resolve(file)) == null)
            throw new ApiException(415, "This file type cannot be edited as text. Download it to edit locally.");
        if (file.Size > MaxBytes) throw new ApiException(413, "This file exceeds the 512 KB text editing limit. Download it to edit locally.");
        if (file.Version != expectedVersion) throw new ApiException(409, "This file has changed. Reopen it before saving your changes.");
        if (content.Contains('\0')) throw new ApiException(400, "Plain text cannot contain binary null characters.");
        byte[] bytes;
        try { bytes = Utf8.GetBytes(content); }
        catch (EncoderFallbackException) { throw new ApiException(400, "The text must contain valid Unicode characters."); }
        if (bytes.Length > MaxBytes) throw new ApiException(413, "Text must be 512 KB or smaller when encoded as UTF-8.");
        // Protect encoding too: UTF-16/binary uploads must not silently become UTF-8 on save.
        using (var original = await storage.Get(file.ObjectKey))
        {
            try { if (Utf8.GetString(original.ToArray()).Contains('\0')) throw new ApiException(415, "Binary files cannot be edited as plain text."); }
            catch (DecoderFallbackException) { throw new ApiException(415, "Only UTF-8 text files can be edited in the browser."); }
        }
        string? newKey = null;
        var oldKey = file.ObjectKey;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            using var stream = new MemoryStream(bytes, false);
            newKey = await versions.Replace(file, actor, expectedVersion, stream, bytes.Length, $"text:{Guid.NewGuid():N}", cancellationToken, allowEmpty: true);
            audit.Add(actor, "EDIT_FILE", id, "file", $"Edited {file.Name}, version {file.Version}");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            if (newKey != null)
                try { await storage.Remove(newKey); } catch (Exception cleanup) { logger.LogWarning(cleanup, "Could not remove failed text version {ObjectKey}", newKey); }
            if (ex is DbUpdateConcurrencyException) throw new ApiException(409, "This file has changed. Reopen it before saving your changes.");
            throw;
        }
        await versions.RemoveUnused(oldKey);
    }
}
