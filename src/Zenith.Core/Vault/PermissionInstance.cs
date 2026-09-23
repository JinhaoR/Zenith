using Zenith.Core.Navigation;

namespace Zenith.Core.Vault;

/// <summary>A durable identity and scope snapshot, including instances no longer in active policy.</summary>
public sealed record PermissionInstance(Guid Id, string Host, AccessClass AccessClass, bool IncludeSubdomains,
    long CreatedPolicyRevision);

public enum PermissionAttributionSource { InitialPolicy, LegacyMigration, ManualVaultChange, ServiceApproval }

/// <summary>Identifies one exact relationship in a service approval's immutable frozen proposal.</summary>
public sealed record ApprovedRequirementReference(string Hostname, int RelationshipIndex);

/// <summary>Historical contribution to a permission instance; never an authorization decision.</summary>
public sealed record PermissionAttribution(Guid PermissionInstanceId, PermissionAttributionSource Source,
    long RecordedPolicyRevision, Guid? OriginatingVaultOperationId = null, Guid? ServiceApprovalId = null,
    ApprovedRequirementReference? Requirement = null);
