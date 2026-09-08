using Zenith.Core.Navigation;

namespace Zenith.Core.Access;

public sealed record AccessGrant
{
    public AccessGrant(SiteIdentity site, DateTimeOffset startsAt, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (expiresAt <= startsAt)
        {
            throw new ArgumentException("A grant must expire after it starts.", nameof(expiresAt));
        }

        Site = site;
        StartsAt = startsAt;
        ExpiresAt = expiresAt;
    }

    public SiteIdentity Site { get; }
    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset ExpiresAt { get; }

    public bool Covers(SiteIdentity site, DateTimeOffset now) =>
        Site == site && now >= StartsAt && now < ExpiresAt;
}
