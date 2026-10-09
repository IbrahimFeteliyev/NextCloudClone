using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Atlas.Api.Auth;
using Atlas.Api.Data;
using Atlas.Api.Entities;
using Atlas.Api.Permissions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Atlas.Api.Services;

public static class FileContentTypes
{
    public static string Resolve(Document file)
    {
        var mime = MediaTypeHeaderValue.TryParse(file.ContentType, out var parsed) ? parsed.MediaType.ToString().ToLowerInvariant() : "application/octet-stream";
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (TextFileTypes.Resolve(file.Name, mime) is { } textType) return textType.Mime;
        if (mime == "text/plain" && extension is ".md" or ".markdown") return "text/markdown";
        if (mime == "text/plain" && extension == ".csv") return "text/csv";
        if (mime != "application/octet-stream") return mime;
        return extension switch
        {
            ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm", ".ogg" or ".ogv" => "video/ogg", ".mov" => "video/quicktime",
            ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf", ".md" or ".markdown" => "text/markdown", ".txt" or ".log" => "text/plain", ".csv" => "text/csv",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation", _ => mime
        };
    }
}

public record PreviewTicket(Guid Id, string Name, string ContentType, long Size, int Version, string ContentPath, DateTime ExpiresAt, Access Permissions = Access.None, int TextEditLimit = TextContentService.MaxBytes);

// Native video elements cannot attach our session's Bearer header. A separate, short-lived
// JWT authorizes only this file/version's content endpoint, and never replaces READ checks.
public class FileContentService(AppDbContext db, CurrentUser current, PermissionService permissions, IConfiguration config)
{
    private const string Audience = "atlas-file-preview";
    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key missing.")));
    public async Task<Document> RequireFile(Guid id, Guid actor)
    {
        await permissions.Require(actor, "file", id, Access.Read);
        return (await db.Files.FindAsync(id))!;
    }
    public async Task<PreviewTicket> CreateTicket(Guid id)
    {
        var file = await RequireFile(id, current.Id);
        var expires = DateTime.UtcNow.AddMinutes(15);
        var jwt = new JwtSecurityToken(issuer: config["Jwt:Issuer"], audience: Audience,
            claims: [new("sub", current.Id.ToString()), new("file", id.ToString()), new("version", file.Version.ToString()), new("purpose", "content")],
            notBefore: DateTime.UtcNow, expires: expires, signingCredentials: new(SigningKey, SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return new(id, file.Name, FileContentTypes.Resolve(file), file.Size, file.Version,
            $"/files/{id}/content?preview_token={Uri.EscapeDataString(token)}", expires, await permissions.FileAccess(current.Id, id));
    }
    public (Guid Actor, int Version) ValidateTicket(string token, Guid id)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = SigningKey, ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = Audience, ValidateLifetime = true, RequireExpirationTime = true,
                ClockSkew = TimeSpan.Zero, ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
            }, out _);
            if (principal.FindFirstValue("purpose") != "content" || principal.FindFirstValue("file") != id.ToString()
                || !Guid.TryParse(principal.FindFirstValue("sub"), out var actor) || !int.TryParse(principal.FindFirstValue("version"), out var version))
                throw new SecurityTokenException();
            return (actor, version);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        { throw new ApiException(401, "This preview has expired or is invalid. Close it and open the file again."); }
    }
}

public record ContentRange(long Offset, long Length, bool Partial)
{
    public static ContentRange Parse(string? header, long size)
    {
        if (string.IsNullOrEmpty(header)) return new(0, size, false);
        if (!RangeHeaderValue.TryParse(header, out var range) || range.Unit != "bytes" || range.Ranges.Count != 1 || size == 0)
            throw new ApiException(416, "Requested byte range is not available.");
        var item = range.Ranges.Single();
        var start = item.From ?? Math.Max(0, size - (item.To ?? 0));
        var end = item.From.HasValue ? Math.Min(item.To ?? size - 1, size - 1) : size - 1;
        if (start >= size || end < start) throw new ApiException(416, "Requested byte range is not available.");
        return new(start, end - start + 1, true);
    }
}
