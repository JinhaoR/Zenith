using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

/// <summary>An explicit user exception, not curated evidence or service-scoped authorization.</summary>
public sealed record LocalServiceExtensionProposal
{
    public LocalServiceExtensionProposal(string serviceId, string serviceName, string serviceEntryPointHostname,
        string hostname, string purpose, long policyRevision)
    {
        ServiceId = RegistryFields.Id(serviceId);
        ServiceName = RegistryFields.Text(serviceName, nameof(ServiceName), 200);
        if (!SiteIdentity.TryCreate(serviceEntryPointHostname, out var entry)) throw new ArgumentException("Invalid service context.");
        ServiceEntryPointHostname = entry.Host;
        if (!SiteIdentity.TryCreate(hostname, out var identity)) throw new ArgumentException("Enter an exact hostname, without a URL, port or wildcard.");
        Hostname = identity.Host;
        Purpose = RegistryFields.Text(purpose, nameof(Purpose), 500);
        if (policyRevision < 0) throw new ArgumentException("Invalid policy revision.");
        PolicyRevision = policyRevision;
    }

    public string ServiceId { get; }
    public string ServiceName { get; }
    public string ServiceEntryPointHostname { get; }
    public string Hostname { get; }
    public string Purpose { get; }
    public long PolicyRevision { get; }
    public string ProposalId => RegistryContentIdentity.LocalExtensionProposal(this);

    public static LocalServiceExtensionProposal Prepare(ServiceRegistry registry, string serviceId, string hostname,
        string purpose, SitePolicySnapshot policy)
    {
        var service = registry.FindService(serviceId) ?? throw new ArgumentException("Choose an existing catalog service.");
        var entry = service.EntryPoints.OrderBy(d => d.Hostname, StringComparer.Ordinal)
            .FirstOrDefault(d => policy.Classify(new SitePolicyEntry(d.Hostname, AccessClass.Whitelist).Identity) == AccessClass.Whitelist);
        if (entry is null)
            throw new ArgumentException("A local exception needs an existing service entry point in your Sphere.");
        return new(service.Id, service.Name, entry.Hostname, hostname, purpose, policy.Revision);
    }
}
