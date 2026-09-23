using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

/// <summary>Ephemeral explanation only. No URI queries, credentials, persistence or approval operations.</summary>
public sealed record UnknownAuthenticationSuggestion(string ServiceId, string ServiceName, string Hostname)
{
    public string Explanation => $"While using {ServiceName}, Zenith blocked {Hostname}. This may be related to authentication, but Zenith has not reviewed this endpoint.";

    // A future native adapter supplies the last committed hostname, never a requested
    // URL or page-supplied claim of service identity. Ambiguous contexts remain generic.
    public static UnknownAuthenticationSuggestion? Create(ServiceRegistry registry, SitePolicySnapshot policy,
        string lastCommittedHostname, string destinationHostname, NavigationDecision decision)
    {
        if (decision is not NavigationDecision.Denied { Reason: NavigationDenialReason.Greylisted } ||
            !SiteIdentity.TryCreate(lastCommittedHostname, out var source) ||
            !SiteIdentity.TryCreate(destinationHostname, out var destination) ||
            policy.Classify(source) != AccessClass.Whitelist || policy.Classify(destination) != AccessClass.Greylist)
            return null;
        if (registry.Infrastructure.Any(i => i.Domains.Any(d => d.Hostname == destination.Host)) ||
            registry.Services.Any(s => s.EntryPoints.Any(d => d.Hostname == destination.Host))) return null;
        var services = registry.Services.Where(s => s.EntryPoints.Any(d => d.Hostname == source.Host)).ToArray();
        return services.Length == 1 ? new(services[0].Id, services[0].Name, destination.Host) : null;
    }
}
