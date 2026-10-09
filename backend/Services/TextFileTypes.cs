using System.Text.Json;

namespace Atlas.Api.Services;

public record TextFileType(string[] Extensions, string Language, string Label, string Mime, string[] Aliases, bool RenderedPreview = false);

// The same registry drives frontend routing/language selection and backend save policy.
public static class TextFileTypes
{
    public static readonly IReadOnlyList<TextFileType> All = JsonSerializer.Deserialize<TextFileType[]>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "text-file-types.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Dictionary<string, TextFileType> ByExtension = All.SelectMany(type => type.Extensions.Select(extension => (extension, type)))
        .ToDictionary(entry => entry.extension, entry => entry.type, StringComparer.OrdinalIgnoreCase);
    public static TextFileType? Resolve(string name, string mime)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(name).TrimStart('.'), out var type)) return null;
        return mime is "application/octet-stream" or "text/plain" || mime == type.Mime || type.Aliases.Contains(mime) ? type : null;
    }
}
