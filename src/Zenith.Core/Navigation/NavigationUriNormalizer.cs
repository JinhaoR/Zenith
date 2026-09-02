using System.Diagnostics.CodeAnalysis;

namespace Zenith.Core.Navigation;

public static class NavigationUriNormalizer
{
    public static bool TryNormalize(
        string? requestedTarget,
        [NotNullWhen(true)] out NormalizedNavigationTarget? normalizedTarget)
    {
        normalizedTarget = null;
        if (string.IsNullOrWhiteSpace(requestedTarget) || requestedTarget.Any(char.IsControl))
        {
            return false;
        }

        if (!Uri.TryCreate(requestedTarget, UriKind.Absolute, out var target) ||
            (!target.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !target.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(target.UserInfo) ||
            !SiteIdentity.TryCreate(target.IdnHost, out var site))
        {
            return false;
        }

        try
        {
            var builder = new UriBuilder(target)
            {
                Scheme = target.Scheme.ToLowerInvariant(),
                Host = site.Host
            };

            if (target.IsDefaultPort)
            {
                builder.Port = -1;
            }

            normalizedTarget = new NormalizedNavigationTarget(builder.Uri, site);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
