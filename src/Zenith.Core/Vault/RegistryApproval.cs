using Zenith.Core.Registry;

namespace Zenith.Core.Vault;

/// <summary>Historical baseline activation; created IDs are for presentation, never authorization.</summary>
public sealed record InfrastructureActivation
{
    public InfrastructureActivation(InfrastructureBaselineProposal proposal, Guid vaultOperationId, DateTimeOffset approvedAt,
        long appliedPolicyRevision, IReadOnlyList<Guid> createdPermissionIds)
    {
        Proposal = proposal ?? throw new ArgumentNullException(nameof(proposal));
        VaultOperationId = vaultOperationId;
        ApprovedAt = approvedAt;
        AppliedPolicyRevision = appliedPolicyRevision;
        CreatedPermissionIds = RegistryFields.Copy(createdPermissionIds, nameof(CreatedPermissionIds));
    }
    public InfrastructureBaselineProposal Proposal { get; }
    public Guid VaultOperationId { get; }
    public DateTimeOffset ApprovedAt { get; }
    public long AppliedPolicyRevision { get; }
    public IReadOnlyList<Guid> CreatedPermissionIds { get; }
}

/// <summary>User-approved local explanation. Removing its permission retires it without deleting history.</summary>
public sealed record LocalServiceExtension(LocalServiceExtensionProposal Proposal, Guid VaultOperationId,
    DateTimeOffset ApprovedAt, long AppliedPolicyRevision, Guid PermissionInstanceId);
