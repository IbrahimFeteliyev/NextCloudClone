using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.Api.Services;
public class OnlyOfficeTokens(OnlyOfficeOptions options)
{
    private readonly JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
    public string Sign(object value, DateTime expires)
    {
        options.Validate();
        var payload = new JwtPayload();
        foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!) payload.Add(entry.Key, entry.Value);
        payload["exp"] = new DateTimeOffset(expires).ToUnixTimeSeconds();
        return handler.WriteToken(new JwtSecurityToken(new JwtHeader(new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)), SecurityAlgorithms.HmacSha256)), payload));
    }
    public JsonElement Validate(string? token, bool requireExpiry)
    {
        options.Validate();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 65536) throw new ApiException(401, "Invalid ONLYOFFICE token.");
        try
        {
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret)),
                ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
                RequireExpirationTime = requireExpiry, RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out var validated);
            return JsonDocument.Parse(((JwtSecurityToken)validated).Payload.SerializeToJson()).RootElement.Clone();
        }
        catch (Exception e) when (e is SecurityTokenException or ArgumentException)
        { throw new ApiException(401, "Invalid or expired ONLYOFFICE token."); }
    }
    public JsonElement Access(string? token, string purpose, Guid fileId)
    {
        var payload = Validate(token, true);
        if (payload.String("purpose") != purpose || payload.String("fileId") != fileId.ToString())
            throw new ApiException(401, "The ONLYOFFICE token is not valid for this document or endpoint.");
        return payload;
    }
    public JsonElement Callback(JsonElement body, string? authorization)
    {
        var bodyToken = body.String("token");
        var headerToken = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true ? authorization[7..] : null;
        var payload = Validate(bodyToken ?? headerToken, false);
        // Header tokens wrap the signed callback in `payload`; body tokens sign it directly.
        if (payload.TryGetProperty("payload", out var nested)) payload = nested;
        if (payload.ValueKind != JsonValueKind.Object || payload.String("key") == null || payload.Int("status") == null)
            throw new ApiException(401, "The token is not a signed ONLYOFFICE callback.");
        // Use only signed fields. Unsigned URL/status/users in the HTTP body are never trusted.
        return payload;
    }
}
internal static class OfficeJson
{
    public static string? String(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    public static int? Int(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var result) ? result : null;
}
