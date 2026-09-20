using Zenith.Core.Navigation;

namespace Zenith.Core.Access;

public sealed record AccessGrant
{
    public AccessGrant(SiteIdentity site, DateTimeOffset startsAt, DateTimeOffset expiresAt, bool includeWwwAlias = false)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (expiresAt <= startsAt)
        {
            throw new ArgumentException("A grant must expire after it starts.", nameof(expiresAt));
        }

        Site = site;
        WwwAlias = includeWwwAlias ? GetWwwAlias(site) : null;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
    }

    public SiteIdentity Site { get; }
    public SiteIdentity? WwwAlias { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public bool Covers(SiteIdentity site, DateTimeOffset now) =>
        site is not null && (Site == site || WwwAlias == site) && now >= StartsAt && now < ExpiresAt;

    private static SiteIdentity? GetWwwAlias(SiteIdentity site)
    {
        // A single, symmetric pair; never a wildcard or a registrable-domain guess.
        if (System.Net.IPAddress.TryParse(site.Host, out _)) return null;
        var bare = site.Host.StartsWith("www.", StringComparison.Ordinal) ? site.Host[4..] : site.Host;
        if (bare.StartsWith("www.", StringComparison.Ordinal) || System.Net.IPAddress.TryParse(bare, out _)) return null;
        return SiteIdentity.TryCreate(site.Host == bare ? "www." + bare : bare, out var alias) ? alias : null;
    }
}
