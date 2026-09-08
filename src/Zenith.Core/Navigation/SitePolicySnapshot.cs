namespace Zenith.Core.Navigation;

public sealed class SitePolicySnapshot
{
    public SitePolicySnapshot(
        IEnumerable<SitePolicyEntry> entries,
        long revision = 0, Zenith.Core.Filtering.HostsBlacklist? mandatoryBlacklist = null)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        var entryCopy = entries.ToArray();
        if (entryCopy.Any(static entry => entry is null))
        {
            throw new ArgumentException("Policy entries cannot contain null values.", nameof(entries));
        }

        Entries = Array.AsReadOnly(entryCopy);
        Revision = revision;
        _mandatoryBlacklist = mandatoryBlacklist;
    }

    public IReadOnlyList<SitePolicyEntry> Entries { get; }

    public long Revision { get; }
    private readonly Zenith.Core.Filtering.HostsBlacklist? _mandatoryBlacklist;

    public AccessClass Classify(SiteIdentity site)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (_mandatoryBlacklist?.Contains(site) == true) return AccessClass.Blacklist;

        var hasWhitelistMatch = false;
        foreach (var entry in Entries.Where(entry => entry.Matches(site)))
        {
            if (entry.AccessClass == AccessClass.Blacklist)
            {
                return AccessClass.Blacklist;
            }

            hasWhitelistMatch = true;
        }

        return hasWhitelistMatch
            ? AccessClass.Whitelist
            : AccessClass.Greylist;
    }
}
