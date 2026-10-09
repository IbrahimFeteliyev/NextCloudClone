using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.DTOs;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Atlas.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Services;
public class OnlyOfficeService(AppDbContext db, CurrentUser current, PermissionService permissions,
    ObjectStorage storage, FileVersionService versions, AuditService audit, OnlyOfficeOptions options,
    OnlyOfficeTokens tokens, OnlyOfficeClient client, DocumentLocks locks, ILogger<OnlyOfficeService> logger)
{
    public static string DocumentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".docx" => "word", ".xlsx" => "cell", ".pptx" => "slide",
        _ => throw new ApiException(415, "ONLYOFFICE supports DOCX, XLSX, and PPTX in this demo. Download this file instead.")
    };
    public async Task<OfficeConfigDto> Config(Guid fileId, string mode)
    {
        if (mode is not ("auto" or "view" or "edit")) throw new ApiException(400, "Editor mode must be auto, view, or edit.");
        using var lease = await locks.Enter(fileId);
        await permissions.Require(current.Id, "file", fileId, mode == "edit" ? Access.Read | Access.Write : Access.Read);
        var file = (await db.Files.FindAsync(fileId))!; var type = DocumentType(file.Name);
        var access = await permissions.FileAccess(current.Id, fileId);
        bool canEdit = mode != "view" && (access & Access.Write) != 0;
        await client.CheckAvailable();
        var actor = await db.Users.FindAsync(current.Id) ?? throw new ApiException(401, "User no longer exists.");
        var session = await db.OfficeSessions.SingleOrDefaultAsync(x => x.FileId == fileId && x.ClosedAt == null && x.ExpiresAt > DateTime.UtcNow);
        if (session == null)
        {
            // Also rotate after a no-change close or an expired session. Active configs stay stable.
            var key = $"{fileId:N}-v{file.Version}-{Guid.NewGuid():N}";
            session = new() { Key = key, FileId = fileId, InitialVersion = file.Version, InitialObjectKey = file.ObjectKey,
                LastSavedVersion = file.Version, ExpiresAt = DateTime.UtcNow.AddHours(options.SessionHours) };
            db.OfficeSessions.Add(session);
        }
        var participant = await db.OfficeParticipants.FindAsync(session.Key, actor.Id);
        if (participant == null) db.OfficeParticipants.Add(new() { SessionKey = session.Key, UserId = actor.Id, CanEdit = canEdit });
        else if (canEdit) participant.CanEdit = true;
        var downloadToken = tokens.Sign(new { purpose = "office-download", fileId = fileId.ToString(), userId = actor.Id.ToString(), key = session.Key, version = session.InitialVersion }, session.ExpiresAt);
        var callbackToken = tokens.Sign(new { purpose = "office-callback", fileId = fileId.ToString(), key = session.Key }, session.ExpiresAt);
        var url = new Uri(options.ApiUrl, $"api/onlyoffice/files/{fileId}/content?access_token={Uri.EscapeDataString(downloadToken)}").AbsoluteUri;
        var callbackUrl = new Uri(options.ApiUrl, $"api/onlyoffice/files/{fileId}/callback?access_token={Uri.EscapeDataString(callbackToken)}").AbsoluteUri;
        var config = new
        {
            documentType = type,
            document = new
            {
                fileType = Path.GetExtension(file.Name)[1..].ToLowerInvariant(), key = session.Key, title = file.Name, url,
                permissions = new { edit = canEdit, download = true, print = true, copy = true,
                    comment = canEdit, review = canEdit, fillForms = canEdit, modifyContentControl = canEdit, modifyFilter = canEdit }
            },
            editorConfig = new
            {
                mode = canEdit ? "edit" : "view", callbackUrl, lang = "en",
                user = new { id = actor.Id.ToString(), name = actor.Name },
                coEditing = new { mode = "fast", change = false },
                customization = new { autosave = true, forcesave = true }
            },
            width = "100%", height = "100%", type = "desktop"
        };
        var signed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(config))!;
        var result = signed.ToDictionary(x => x.Key, x => (object)x.Value);
        result["token"] = tokens.Sign(config, session.ExpiresAt);
        audit.Add(actor.Id, "OPEN_OFFICE", fileId, "file", $"Opened {file.Name} in {(canEdit ? "edit" : "view")} mode");
        await db.SaveChangesAsync();
        return new(options.BrowserUrl.AbsoluteUri.TrimEnd('/'), canEdit ? "edit" : "view", file.Version, result);
    }
    public async Task<(MemoryStream Stream, string ContentType, string Name)> Content(Guid fileId, string? token)
    {
        var grant = tokens.Access(token, "office-download", fileId);
        if (!Guid.TryParse(grant.String("userId"), out var actor) || grant.Int("version") is not int version)
            throw new ApiException(401, "Invalid document access token.");
        await permissions.Require(actor, "file", fileId, Access.Read);
        var session = await db.OfficeSessions.FindAsync(grant.String("key"));
        if (session == null || session.FileId != fileId || session.ClosedAt != null || session.ExpiresAt <= DateTime.UtcNow || session.InitialVersion != version)
            throw new ApiException(403, "This editing session is closed or expired. Reopen the document.");
        var file = (await db.Files.FindAsync(fileId))!;
        return (await storage.Get(session.InitialObjectKey), file.ContentType, file.Name);
    }
    public async Task Callback(Guid fileId, string? accessToken, JsonElement body, string? authorization)
    {
        var grant = tokens.Access(accessToken, "office-callback", fileId);
        var callback = tokens.Callback(body, authorization);
        var key = callback.String("key"); var status = callback.Int("status")!.Value;
        if (key != grant.String("key")) throw new ApiException(401, "Callback document key does not match this session.");
        using var lease = await locks.Enter(fileId);
        var session = await db.OfficeSessions.FindAsync(key) ?? throw new ApiException(404, "Editing session not found.");
        var file = await db.Files.FindAsync(fileId) ?? throw new ApiException(404, "File not found.");
        if (file.DeletedAt != null) throw new ApiException(404, "File is in Trash.");
        if (session.FileId != fileId || session.ExpiresAt <= DateTime.UtcNow) throw new ApiException(403, "Editing session expired.");
        if (status is 3 or 7) { logger.LogWarning("ONLYOFFICE reported save failure for {FileId}, status {Status}", fileId, status); throw new ApiException(502, "ONLYOFFICE reported a document save error. The previous version is preserved."); }
        if (status is 1 or 4)
        {
            if (status == 4 && session.ClosedAt == null) { session.ClosedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
            return;
        }
        if (status is not (2 or 6)) throw new ApiException(400, "Unsupported ONLYOFFICE callback status.");
        var url = callback.String("url");
        if (string.IsNullOrEmpty(url)) throw new ApiException(400, "Save callback is missing its signed download URL.");
        var saveId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{status}|{url}")));
        if (await db.OfficeSaves.AnyAsync(x => x.FileId == fileId && x.Id == saveId)) return;
        if (session.ClosedAt != null) throw new ApiException(409, "This editing session has already closed.");
        if (!callback.TryGetProperty("users", out var users) || users.ValueKind != JsonValueKind.Array || users.GetArrayLength() == 0)
            throw new ApiException(403, "A save must identify an authorized editor.");
        Guid actor = Guid.Empty;
        foreach (var editor in users.EnumerateArray())
        {
            if (editor.ValueKind != JsonValueKind.String || !Guid.TryParse(editor.GetString(), out var editorId)) throw new ApiException(403, "Invalid editor identity.");
            var participant = await db.OfficeParticipants.FindAsync(key, editorId);
            if (participant?.CanEdit != true) throw new ApiException(403, "Read-only users cannot save changes.");
            await permissions.Require(editorId, "file", fileId, Access.Read | Access.Write);
            if (actor == Guid.Empty) actor = editorId;
        }
        var extension = Path.GetExtension(file.Name)[1..].ToLowerInvariant();
        if (callback.String("filetype") is string fileType && fileType != extension) throw new ApiException(400, "ONLYOFFICE returned an unexpected file type.");
        using var content = await client.Download(url);
        ValidateDocument(content, extension);
        await using var transaction = await db.Database.BeginTransactionAsync();
        string? newObject = null;
        var oldKey = file.ObjectKey;
        try
        {
            newObject = await versions.Replace(file, actor, session.LastSavedVersion, content, content.Length, saveId);
            session.LastSavedVersion = file.Version;
            db.OfficeSaves.Add(new() { Id = saveId, FileId = fileId });
            audit.Add(actor, "EDIT_FILE", fileId, "file", $"Saved {file.Name} from ONLYOFFICE");
            if (status == 2) session.ClosedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            if (newObject != null) { try { await storage.Remove(newObject); } catch (Exception ex) { logger.LogWarning(ex, "Unable to clean up failed ONLYOFFICE save object"); } }
            throw;
        }
        await versions.RemoveUnused(oldKey);
        if (session.ClosedAt != null) await versions.RemoveUnused(session.InitialObjectKey);
    }
    private static void ValidateDocument(MemoryStream stream, string extension)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var main = extension switch { "docx" => "word/document.xml", "xlsx" => "xl/workbook.xml", "pptx" => "ppt/presentation.xml", _ => "" };
            if (archive.GetEntry("[Content_Types].xml") == null || archive.GetEntry(main) == null)
                throw new ApiException(502, "ONLYOFFICE returned an invalid Office document. The previous version is preserved.");
        }
        catch (InvalidDataException) { throw new ApiException(502, "ONLYOFFICE returned an invalid Office document. The previous version is preserved."); }
        finally { stream.Position = 0; }
    }
}
