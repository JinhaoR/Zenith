namespace Zenith.Core.Registry;

/// <summary>A service-to-infrastructure relationship, not a permission requirement.</summary>
public sealed record InfrastructureDependency
{
    public InfrastructureDependency(string infrastructureId, RelationshipProvenance? provenance = null)
    {
        InfrastructureId = RegistryFields.Id(infrastructureId, nameof(InfrastructureId));
        Provenance = provenance;
    }

    public string InfrastructureId { get; }
    public RelationshipProvenance? Provenance { get; }
}
