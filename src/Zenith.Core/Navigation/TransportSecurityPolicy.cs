using System.Net;

namespace Zenith.Core.Navigation;

/// <summary>Fixed transport restrictions, independent of Whitelist membership and grants.</summary>
public static class TransportSecurityPolicy
{
    public static bool Allows(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Any(char.IsControl)) return false;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is "ws" or "wss")
            address = new UriBuilder(uri) { Scheme = uri.Scheme == "wss" ? "https" : "http" }.Uri.AbsoluteUri;
        if (!NavigationUriNormalizer.TryNormalize(address, out var target)) return false;
        if (target.Target.Scheme == Uri.UriSchemeHttps) return true;

        // Never resolve a hostname to decide this exception: DNS can change. Local
        // development still requires ordinary site authorization, not just loopback.
        return IPAddress.TryParse(target.Target.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);
    }
}
