namespace Zenith.Core.Navigation;

/// <summary>Engine-address identity for native presentation, never a site-trust decision.</summary>
public sealed record WebsiteIdentity(string Origin, bool UsesHttps)
{
    public static WebsiteIdentity? FromAddress(string? address)
    {
        if (!NavigationUriNormalizer.TryNormalize(address ?? string.Empty, out var target)) return null;
        var uri = target.Target;
        var origin = new UriBuilder(uri.Scheme, target.Site.Host, uri.IsDefaultPort ? -1 : uri.Port)
            .Uri.GetLeftPart(UriPartial.Authority);
        return new(origin, uri.Scheme == Uri.UriSchemeHttps);
    }
}
