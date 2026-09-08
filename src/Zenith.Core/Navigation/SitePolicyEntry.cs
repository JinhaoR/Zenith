namespace Zenith.Core.Navigation;

public sealed record SitePolicyEntry
{
    public SitePolicyEntry(
        string host,
        AccessClass accessClass,
        bool includeSubdomains = false, string? displayName = null)
    {
        if (!SiteIdentity.TryCreate(host, out var identity))
        {
            throw new ArgumentException("The policy entry host is not valid.", nameof(host));
        }

        if (!Enum.IsDefined(accessClass))
        {
            throw new ArgumentOutOfRangeException(nameof(accessClass));
        }

        if (accessClass == AccessClass.Greylist)
        {
            throw new ArgumentException(
                "Greylist is the default classification and is not stored as a policy entry.",
                nameof(accessClass));
        }

        Identity = identity;
        AccessClass = accessClass;
        IncludeSubdomains = includeSubdomains;
        DisplayName = displayName;
    }

    public SiteIdentity Identity { get; }

    public AccessClass AccessClass { get; }

    public bool IncludeSubdomains { get; }
    public string? DisplayName { get; }

    public bool Matches(SiteIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        return candidate == Identity ||
               (IncludeSubdomains && candidate.IsSameOrSubdomainOf(Identity));
    }
}
