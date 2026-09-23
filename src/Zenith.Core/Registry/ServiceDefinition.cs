namespace Zenith.Core.Registry;

public enum ServiceStatus { Initial, Experimental }

/// <summary>Catalog metadata only; this is neither a service grant nor a policy entry.</summary>
public sealed record ServiceDefinition
{
    public ServiceDefinition(string id, string name, string category, string description,
        IEnumerable<string> capabilities, IEnumerable<DomainRequirement> domains,
        IEnumerable<InfrastructureDependency> infrastructureDependencies, ServiceStatus status,
        IEnumerable<string>? ecosystems = null)
    {
        Id = RegistryFields.Id(id);
        Name = RegistryFields.Text(name, nameof(Name), 200);
        Category = RegistryFields.Id(category, nameof(Category));
        Description = RegistryFields.Text(description, nameof(Description));
        Capabilities = RegistryFields.Ids(capabilities, nameof(Capabilities));
        Domains = RegistryFields.Copy(domains, nameof(Domains));
        InfrastructureDependencies = RegistryFields.Copy(infrastructureDependencies, nameof(InfrastructureDependencies));
        if (InfrastructureDependencies.Select(d => d.InfrastructureId).Distinct(StringComparer.Ordinal).Count() != InfrastructureDependencies.Count)
            throw new ArgumentException("InfrastructureDependencies contains duplicate references.", nameof(infrastructureDependencies));
        if (!Enum.IsDefined(status)) throw new ArgumentException("Unknown service status.", nameof(status));
        Status = status;
        Ecosystems = RegistryFields.Ids(ecosystems ?? [], nameof(Ecosystems));
    }

    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public IReadOnlyList<string> Capabilities { get; }
    public IReadOnlyList<DomainRequirement> Domains { get; }
    /// <summary>Visible service endpoints. Shared infrastructure is proposed independently.</summary>
    public IReadOnlyList<DomainRequirement> EntryPoints => Domains;
    /// <summary>Optional descriptive associations retained for catalog and historical compatibility.</summary>
    public IReadOnlyList<InfrastructureDependency> InfrastructureDependencies { get; }
    public ServiceStatus Status { get; }
    public IReadOnlyList<string> Ecosystems { get; }
}
