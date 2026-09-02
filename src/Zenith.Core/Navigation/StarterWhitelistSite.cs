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

        if (!NavigationUriNormalizer.TryNormalize(target, out var normalizedTarget) ||
            normalizedTarget.Site != identity)
        {
            throw new ArgumentException(
                "The starter site target must be an HTTP(S) address for its configured host.",
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
