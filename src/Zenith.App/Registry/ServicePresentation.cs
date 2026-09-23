using System.Globalization;
using Zenith.Core.Registry;

namespace Zenith.App.Registry;

internal static class RegistryLabels
{
    internal static string Category(string id) => id == "ai" ? "AI" :
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('_', ' '));

    internal static string Evidence(RelationshipProvenance? evidence)
    {
        if (evidence is null) return "No provenance recorded.";
        var source = evidence.SourceType switch
        {
            RegistrySourceType.OfficialDocumentation => "Official documentation",
            RegistrySourceType.ObservedDependency => "Observed dependency",
            RegistrySourceType.UserAdded => "User-added",
            _ => evidence.SourceType.ToString()
        };
        var lines = new List<string> { $"Source: {source}" };
        if (evidence.SourceReference is { } reference) lines.Add($"Reference: {reference}");
        if (evidence.Review is { } review)
            lines.Add($"Reviewed by: {review.ReviewedBy}\nReviewed at: {review.ReviewedAt.ToString("O", CultureInfo.InvariantCulture)}");
        if (evidence.Explanation is { } explanation) lines.Add($"Explanation: {explanation}");
        return string.Join("\n", lines);
    }
}

internal sealed record DomainPresentation(DomainRequirement Requirement)
{
    public string Hostname => Requirement.Hostname;
    public string Purpose => Requirement.Purpose;
    public string RequiredLabel => Requirement.Required ? "Required: yes (catalog description)" : "Required: no (optional)";
    public string Classification => Requirement.DependencyType switch
    {
        DependencyType.Navigation => "navigation",
        DependencyType.Authentication => "authentication",
        DependencyType.Api => "api",
        DependencyType.EmbeddedResource => "embedded_resource",
        _ => "unknown"
    };
    public string Evidence => RegistryLabels.Evidence(Requirement.Provenance);
    public string Applicability => Requirement.PermissionApplicability.ToString();
}

internal sealed record InfrastructurePresentation(string Id, string Name, string Description,
    IReadOnlyList<DomainPresentation> Domains, RelationshipProvenance? RelationshipEvidence)
{
    public string Evidence => RegistryLabels.Evidence(RelationshipEvidence);
}

internal sealed class ServicePresentation
{
    internal ServicePresentation(ServiceDefinition service, ServiceRegistry registry)
    {
        Id = service.Id;
        Name = service.Name;
        CategoryId = service.Category;
        Description = service.Description;
        Status = service.Status;
        Capabilities = Array.AsReadOnly(service.Capabilities.Select(id => registry.Capabilities.Single(c => c.Id == id)).ToArray());
        Domains = Array.AsReadOnly(service.Domains.Select(d => new DomainPresentation(d)).ToArray());
        Infrastructure = Array.AsReadOnly(service.InfrastructureDependencies.Select(edge =>
        {
            var definition = registry.Infrastructure.Single(i => i.Id == edge.InfrastructureId);
            return new InfrastructurePresentation(definition.Id, definition.Name, definition.Description,
                Array.AsReadOnly(definition.Domains.Select(d => new DomainPresentation(d)).ToArray()), edge.Provenance);
        }).ToArray());
    }

    public string Id { get; }
    public string Name { get; }
    public string CategoryId { get; }
    public string CategoryName => RegistryLabels.Category(CategoryId);
    public string Description { get; }
    public ServiceStatus Status { get; }
    public bool IsExperimental => Status == ServiceStatus.Experimental;
    public string DisplayName => IsExperimental ? $"{Name} · Experimental" : Name;
    public string StatusDescription => IsExperimental ? "Experimental catalog entry; this is not a permission or security rating." :
        "Initial catalog entry; service compatibility is not guaranteed.";
    public string MembershipLabel => "Available in Registry";
    public IReadOnlyList<CapabilityDefinition> Capabilities { get; }
    public IReadOnlyList<DomainPresentation> Domains { get; }
    public IReadOnlyList<InfrastructurePresentation> Infrastructure { get; }
    public string DependencySummary => Infrastructure.Count == 0 ? "No shared infrastructure association recorded." :
        string.Join(", ", Infrastructure.Select(i => i.Name)) + " (descriptive association; activation is separate)";
}

internal sealed record ServiceCategoryPresentation(string Id, string Name, IReadOnlyList<ServicePresentation> Services);
