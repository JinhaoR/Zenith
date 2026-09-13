using System.Text.Json;

namespace Zenith.App.Filtering;

/// <summary>Untrusted presentation hints only; never a native command dispatcher.</summary>
internal sealed record CosmeticMessage(string Kind, string Token, string Url, string[] Classes, string[] Ids, string[] Hrefs)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static CosmeticMessage? Read(string json, string source)
    {
        if (json.Length > 256 * 1024 || source.Length > 32768 ||
            !Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        try
        {
            var query = JsonSerializer.Deserialize<CosmeticMessage>(json, Options);
            return query is not null && query.Kind == "zenith-cosmetics" &&
                query.Token is { Length: > 0 and <= 64 } && query.Url == source &&
                Valid(query.Classes, 512, 128) && Valid(query.Ids, 512, 128) && Valid(query.Hrefs, 128, 512)
                ? query : null;
        }
        catch (JsonException) { return null; }
    }

    internal static bool IsCurrentDocument(string source, string currentSource) =>
        string.Equals(source, currentSource, StringComparison.Ordinal);

    private static bool Valid(string[]? values, int count, int length) =>
        values is not null && values.Length <= count && values.All(value => value is not null && value.Length <= length);
}
