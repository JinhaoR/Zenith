using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

public enum DependencyType { Unknown, Navigation, Authentication, Api, EmbeddedResource }
public enum PermissionApplicability { Unknown, RequiredNavigation, OptionalNavigation, BackgroundOnly }

/// <summary>Descriptive host association. Required does not grant or require a permission.</summary>
public sealed record DomainRequirement
{
    public DomainRequirement(string hostname, string purpose, bool required,
        DependencyType dependencyType = DependencyType.Unknown, RelationshipProvenance? provenance = null,
        PermissionApplicability permissionApplicability = PermissionApplicability.Unknown)
    {
        if (!SiteIdentity.TryCreate(hostname, out var identity))
            throw new ArgumentException("A domain requirement must contain a hostname, without a URL, wildcard, path or port.", nameof(hostname));
        Hostname = identity.Host;
        Purpose = RegistryFields.Text(purpose, nameof(Purpose));
        Required = required;
        if (!Enum.IsDefined(dependencyType)) throw new ArgumentException("Unknown dependency type.", nameof(dependencyType));
        DependencyType = dependencyType;
        Provenance = provenance;
        if (!Enum.IsDefined(permissionApplicability)) throw new ArgumentException("Unknown permission applicability.");
        if (permissionApplicability == PermissionApplicability.RequiredNavigation && !required ||
            permissionApplicability == PermissionApplicability.OptionalNavigation && required)
            throw new ArgumentException("Navigation applicability must agree with required/optional metadata.");
        PermissionApplicability = permissionApplicability;
    }

    public string Hostname { get; }
    public string Purpose { get; }
    public bool Required { get; }
    public DependencyType DependencyType { get; }
    public RelationshipProvenance? Provenance { get; }
    public PermissionApplicability PermissionApplicability { get; }
}
