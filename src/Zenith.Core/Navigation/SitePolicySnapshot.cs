namespace Zenith.Core.Navigation;

public sealed class SitePolicySnapshot
{
    public SitePolicySnapshot(
        IEnumerable<SitePolicyEntry> entries,
        long revision = 0)
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
    }

    public IReadOnlyList<SitePolicyEntry> Entries { get; }

    public long Revision { get; }

    public AccessClass Classify(SiteIdentity site)
    {
        ArgumentNullException.ThrowIfNull(site);

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
