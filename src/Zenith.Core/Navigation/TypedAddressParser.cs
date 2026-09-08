using System.Diagnostics.CodeAnalysis;

namespace Zenith.Core.Navigation;

/// <summary>Expands user-entered website addresses, never engine navigation targets.</summary>
public static class TypedAddressParser
{
    public static bool TryParse(string? input,
        [NotNullWhen(true)] out NormalizedNavigationTarget? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(input) || input.Any(char.IsControl)) return false;
        var text = input.Trim();
        if (text.Contains('\\')) return false;
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return NavigationUriNormalizer.TryNormalize(text, out target);

        // Require a recognizable host, not a search term, relative path or custom scheme.
        var authority = text.Split(['/', '?', '#'], 2)[0];
        if (authority.Length == 0 || authority.Contains('@') || authority.Any(char.IsWhiteSpace)) return false;
        if (!NavigationUriNormalizer.TryNormalize("https://" + text, out var candidate)) return false;
        var host = candidate.Site.Host;
        if (!host.Contains('.') && !host.Contains(':') && host != "localhost") return false;
        target = candidate;
        return true;
    }
}
