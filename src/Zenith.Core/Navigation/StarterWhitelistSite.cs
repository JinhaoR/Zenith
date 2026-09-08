namespace Zenith.Core.Navigation;

public sealed record StarterWhitelistSite
{
    public StarterWhitelistSite(
        string name,
        string host,
        string target,
        bool includeSubdomains = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!SiteIdentity.TryCreate(host, out var identity))
        {
            throw new ArgumentException("The starter site host is not valid.", nameof(host));
        }

        var entry = new SitePolicyEntry(identity.Host, AccessClass.Whitelist, includeSubdomains);
        if (!NavigationUriNormalizer.TryNormalize(target, out var normalizedTarget) ||
            !entry.Matches(normalizedTarget.Site))
        {
            throw new ArgumentException(
                "The starter site target must be an HTTP(S) address within its configured host scope.",
                nameof(target));
        }

        Name = name;
        Identity = identity;
        Target = normalizedTarget.Target;
        IncludeSubdomains = includeSubdomains;
    }

    public string Name { get; }

    public SiteIdentity Identity { get; }

    public string Host => Identity.Host;

    public Uri Target { get; }

    public bool IncludeSubdomains { get; }
}
