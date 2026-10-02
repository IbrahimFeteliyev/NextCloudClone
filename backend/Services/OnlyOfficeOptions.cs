using System.Text;
namespace Atlas.Api.Services;
public sealed class OnlyOfficeOptions(IConfiguration config)
{
    public string Secret => Environment.GetEnvironmentVariable("OnlyOffice__JwtSecret") ?? "";
    public Uri BrowserUrl => Url("OnlyOffice:DocumentServerUrl");
    public Uri InternalUrl => Url("OnlyOffice:InternalUrl");
    public Uri ApiUrl => Url("OnlyOffice:ApiUrl");
    public int SessionHours => Math.Clamp(config.GetValue("OnlyOffice:SessionHours", 24), 1, 168);
    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(Secret) < 32)
            throw new ApiException(503, "ONLYOFFICE is not configured. Set OnlyOffice__JwtSecret to at least 32 bytes and use the same secret in Document Server.");
        _ = BrowserUrl; _ = InternalUrl; _ = ApiUrl;
    }
    private Uri Url(string key)
    {
        var value = config[key];
        if (!Uri.TryCreate(value?.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ApiException(503, $"Configure {key} with an absolute HTTP(S) URL.");
        return uri;
    }
}
