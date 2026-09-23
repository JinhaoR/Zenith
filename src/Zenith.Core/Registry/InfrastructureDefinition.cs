namespace Zenith.Core.Registry;

public sealed record InfrastructureDefinition
{
    public InfrastructureDefinition(string id, string name, string description, IEnumerable<DomainRequirement> domains)
    {
        Id = RegistryFields.Id(id);
        Name = RegistryFields.Text(name, nameof(Name), 200);
        Description = RegistryFields.Text(description, nameof(Description));
        Domains = RegistryFields.Copy(domains, nameof(Domains));
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<DomainRequirement> Domains { get; }
}
