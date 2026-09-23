using System.Text.Json;
using System.Text.Json.Serialization;
using Zenith.Core.Registry;

namespace Zenith.App.Registry;

internal sealed class RegistryManifestDocument
{
    public required int SchemaVersion { get; init; }
    public required string Revision { get; init; }
    public required string DependencyCoverage { get; init; }
    public required string[] Services { get; init; }
}

internal sealed class CapabilitiesDocument
{
    public required CapabilityDocument[] Capabilities { get; init; }
}

internal sealed class CapabilityDocument
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    internal CapabilityDefinition ToModel() => new(Id, Name, Description);
}

internal sealed class InfrastructureDocument
{
    public required InfrastructureItemDocument[] Infrastructure { get; init; }
}

internal sealed class InfrastructureItemDocument
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required DomainDocument[] Domains { get; init; }
    internal InfrastructureDefinition ToModel() => new(Id, Name, Description, Domains.Select(d => d.ToModel()));
}

internal sealed class DomainDocument
{
    public required string Hostname { get; init; }
    public required string Purpose { get; init; }
    public required bool Required { get; init; }
    public DependencyType DependencyType { get; init; } = DependencyType.Unknown;
    public ProvenanceDocument? Provenance { get; init; }
    public PermissionApplicability PermissionApplicability { get; init; } = PermissionApplicability.Unknown;
    internal DomainRequirement ToModel() => new(Hostname, Purpose, Required, DependencyType, Provenance?.ToModel(), PermissionApplicability);
}

internal sealed class ProvenanceDocument
{
    public required RegistrySourceType SourceType { get; init; }
    public string? SourceReference { get; init; }
    public ReviewDocument? Review { get; init; }
    public string? Explanation { get; init; }
    internal RelationshipProvenance ToModel() => new(SourceType, SourceReference, Review?.ToModel(), Explanation);
}

internal sealed class ReviewDocument
{
    public required string ReviewedBy { get; init; }
    public required DateTimeOffset ReviewedAt { get; init; }
    internal RelationshipReview ToModel() => new(ReviewedBy, ReviewedAt);
}

internal sealed class InfrastructureDependencyDocument
{
    public required string Id { get; init; }
    public ProvenanceDocument? Provenance { get; init; }
    internal InfrastructureDependency ToModel() => new(Id, Provenance?.ToModel());
}

// Preserve schema-1 ID strings while permitting evidence on an explicit relationship object.
internal sealed class InfrastructureDependencyConverter : JsonConverter<InfrastructureDependency>
{
    public override InfrastructureDependency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new(reader.GetString()!);
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("An infrastructure dependency must be an ID or relationship object.");
        return JsonSerializer.Deserialize<InfrastructureDependencyDocument>(ref reader, options)!.ToModel();
    }

    public override void Write(Utf8JsonWriter writer, InfrastructureDependency value, JsonSerializerOptions options) =>
        throw new NotSupportedException("The bundled registry loader is read-only.");
}

internal sealed class ServiceDocument
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string[] Capabilities { get; init; }
    public required DomainDocument[] Domains { get; init; }
    public required InfrastructureDependency[] Requires { get; init; }
    public required ServiceStatus Status { get; init; }
    public string[] Ecosystems { get; init; } = [];
    internal ServiceDefinition ToModel() => new(Id, Name, Category, Description, Capabilities,
        Domains.Select(d => d.ToModel()), Requires, Status, Ecosystems);
}
