using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;

namespace Zenith.Core.Navigation;

public sealed record SiteIdentity
{
    private SiteIdentity(string host)
    {
        Host = host;
    }

    public string Host { get; }

    public static bool TryCreate(
        string? host,
        [NotNullWhen(true)] out SiteIdentity? identity)
    {
        identity = null;
        if (string.IsNullOrWhiteSpace(host) || !host.Equals(host.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = host.TrimEnd('.');
        if (candidate.Length == 0)
        {
            return false;
        }

        var addressCandidate = candidate.TrimStart('[').TrimEnd(']');
        if (IPAddress.TryParse(addressCandidate, out var address))
        {
            identity = new SiteIdentity(address.ToString().ToLowerInvariant());
            return true;
        }

        if (Uri.CheckHostName(candidate) != UriHostNameType.Dns)
        {
            return false;
        }

        try
        {
            var asciiHost = new IdnMapping().GetAscii(candidate).ToLowerInvariant();
            if (Uri.CheckHostName(asciiHost) != UriHostNameType.Dns)
            {
                return false;
            }

            identity = new SiteIdentity(asciiHost);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public bool IsSameOrSubdomainOf(SiteIdentity parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (Host.Equals(parent.Host, StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(Host, out _) || IPAddress.TryParse(parent.Host, out _))
        {
            return false;
        }

        return Host.EndsWith($".{parent.Host}", StringComparison.Ordinal);
    }

    public override string ToString() => Host;
}
