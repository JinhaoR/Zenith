using System.Collections.Frozen;
using Zenith.Core.Navigation;

namespace Zenith.Core.Filtering;

public interface IBlacklistSource
{
    HostsBlacklist? Current { get; }
}

public static class BlacklistRequestPolicy
{
    public static bool IsBlocked(HostsBlacklist? list, string target)
    {
        if (list is null) return true;
        return Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
            SiteIdentity.TryCreate(uri.IdnHost, out var site) && list.Contains(site);
    }
}

public sealed class HostsBlacklist
{
    private readonly FrozenSet<string> _hosts;
    private HostsBlacklist(HashSet<string> hosts) => _hosts = hosts.ToFrozenSet(StringComparer.Ordinal);
    public int Count => _hosts.Count;
    public bool Contains(SiteIdentity site) => _hosts.Contains(site.Host);
    public bool HasSameEntries(HostsBlacklist other) => _hosts.SetEquals(other._hosts);

    public static HostsBlacklist Parse(string text)
    {
        if (text.Length > 32 * 1024 * 1024) throw new ArgumentException("Hosts list is too large.");
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Split('#', 2)[0].Trim();
            if (line.Length == 0) continue;
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) throw new ArgumentException("Invalid hosts record.");
            // Standard loopback/IPv6 boilerplate is not a domain block rule.
            if (fields[0] is "127.0.0.1" or "::1" or "fe80::1" or "fe80::1%lo0" or "ff00::0" or "ff02::1" or "ff02::2" or "ff02::3") continue;
            if (fields.Length == 2 && fields[0] == "255.255.255.255" && fields[1] == "broadcasthost") continue;
            if (fields[0] != "0.0.0.0") throw new ArgumentException("Unexpected hosts address.");
            foreach (var value in fields.Skip(1))
            {
                if (value is "0.0.0.0" or "localhost" or "localhost.localdomain" or "local") continue;
                if (!SiteIdentity.TryCreate(value, out var site) || Uri.CheckHostName(site.Host) != UriHostNameType.Dns || !site.Host.Contains('.'))
                    throw new ArgumentException("Invalid blocked hostname.");
                hosts.Add(site.Host);
                if (hosts.Count > 1000000) throw new ArgumentException("Too many hosts.");
            }
        }
        if (hosts.Count == 0) throw new ArgumentException("Empty hosts list.");
        return new(hosts);
    }
}
