namespace Atlas.Api.Services;
public class OnlyOfficeClient(HttpClient http, OnlyOfficeOptions options)
{
    public async Task CheckAvailable()
    {
        options.Validate();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await http.GetAsync(new Uri(options.InternalUrl, "healthcheck"), timeout.Token);
            if (!response.IsSuccessStatusCode || (await response.Content.ReadAsStringAsync(timeout.Token)).Trim() != "true")
                throw new ApiException(503, "ONLYOFFICE is unavailable or still starting. Please try again shortly.");
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { throw new ApiException(503, "Unable to reach ONLYOFFICE. Start the Document Server and try again."); }
    }
    public async Task<MemoryStream> Download(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var source) || !string.IsNullOrEmpty(source.UserInfo) || source.Fragment.Length != 0)
            throw new ApiException(400, "Invalid ONLYOFFICE save URL.");
        // Document Server may emit its browser-facing address. Both configured origins are trusted,
        // and all requests are routed to the configured internal address. Redirects are disabled.
        var allowed = new[] { options.InternalUrl, options.BrowserUrl };
        if (!allowed.Any(x => x.Scheme == source.Scheme && x.Host == source.Host && x.Port == source.Port) || !source.AbsolutePath.StartsWith("/cache/files/", StringComparison.Ordinal))
            throw new ApiException(400, "The save URL must point to the configured ONLYOFFICE document cache.");
        var target = new Uri(options.InternalUrl, source.PathAndQuery.TrimStart('/'));
        var result = new MemoryStream();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var response = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new ApiException(502, "ONLYOFFICE could not supply the edited document. The saved version was not changed.");
            if (response.Content.Headers.ContentLength > 100 * 1024 * 1024) throw new ApiException(400, "Edited documents must be 100 MB or smaller.");
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81920]; int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (result.Length + count > 100 * 1024 * 1024) throw new ApiException(400, "Edited documents must be 100 MB or smaller.");
                await result.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
            }
            if (result.Length == 0) throw new ApiException(502, "ONLYOFFICE returned an empty document.");
            result.Position = 0; return result;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        { result.Dispose(); throw new ApiException(502, "Failed to download the edited document from ONLYOFFICE. Please retry saving."); }
        catch { result.Dispose(); throw; }
    }
}
