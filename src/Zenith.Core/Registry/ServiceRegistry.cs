using System.Collections.Frozen;
using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

public sealed record DomainAssociation(string ServiceId, string? InfrastructureId, DomainRequirement Domain,
    RelationshipProvenance? InfrastructureProvenance = null);

/// <summary>An immutable catalog snapshot with no connection to authorization state.</summary>
public sealed class ServiceRegistry
{
    private readonly FrozenDictionary<string, ServiceDefinition> _servicesById;
    private readonly FrozenDictionary<string, InfrastructureDefinition> _infrastructureById;

    public ServiceRegistry(string revision, IEnumerable<ServiceDefinition> services,
        IEnumerable<CapabilityDefinition> capabilities, IEnumerable<InfrastructureDefinition> infrastructure)
    {
        Revision = RegistryFields.Text(revision, nameof(Revision), 80);
        Services = RegistryFields.Copy(services, nameof(Services));
        Capabilities = RegistryFields.Copy(capabilities, nameof(Capabilities));
        Infrastructure = RegistryFields.Copy(infrastructure, nameof(Infrastructure));
        var issues = RegistryValidator.Validate(Services, Capabilities, Infrastructure);
        if (issues.Count > 0) throw new ArgumentException(string.Join(" ", issues.Select(i => $"{i.Location}: {i.Message}")));
        _servicesById = Services.ToFrozenDictionary(s => s.Id, StringComparer.Ordinal);
        _infrastructureById = Infrastructure.ToFrozenDictionary(i => i.Id, StringComparer.Ordinal);
    }

    public string Revision { get; }
    public IReadOnlyList<ServiceDefinition> Services { get; }
    public IReadOnlyList<CapabilityDefinition> Capabilities { get; }
    public IReadOnlyList<InfrastructureDefinition> Infrastructure { get; }
    public ServiceDefinition? FindService(string id) => _servicesById.GetValueOrDefault(id);
    public IReadOnlyList<ServiceDefinition> ListServices() => Services;
    public IReadOnlyList<ServiceDefinition> ListServicesByCategory(string category) =>
        Array.AsReadOnly(Services.Where(s => s.Category == category).ToArray());
    public IReadOnlyList<ServiceDefinition> ListServicesByCapability(string capabilityId) =>
        Array.AsReadOnly(Services.Where(s => s.Capabilities.Contains(capabilityId)).ToArray());

    public IReadOnlyList<InfrastructureDefinition> GetInfrastructureDependencies(string serviceId) =>
        Array.AsReadOnly(FindService(serviceId)?.InfrastructureDependencies.Select(d => _infrastructureById[d.InfrastructureId]).ToArray() ?? []);

    /// <summary>Descriptive associations, including legacy infrastructure edges. Not a permission expansion API.</summary>
    public IReadOnlyList<DomainAssociation> GetDomainRequirements(string serviceId)
    {
        if (FindService(serviceId) is not { } service) return Array.Empty<DomainAssociation>();
        return Array.AsReadOnly(service.Domains.Select(d => new DomainAssociation(service.Id, null, d))
            .Concat(service.InfrastructureDependencies.SelectMany(i => _infrastructureById[i.InfrastructureId].Domains
                .Select(d => new DomainAssociation(service.Id, i.InfrastructureId, d, i.Provenance))))
            .ToArray());
    }

    public IReadOnlyList<ServiceDefinition> FindServicesAssociatedWithHostname(string hostname)
    {
        if (!SiteIdentity.TryCreate(hostname, out var identity)) return Array.Empty<ServiceDefinition>();
        return Array.AsReadOnly(Services.Where(s => GetDomainRequirements(s.Id).Any(a => a.Domain.Hostname == identity.Host)).ToArray());
    }
}
