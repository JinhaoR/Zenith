using Zenith.Core.Registry;

namespace Zenith.Core.Vault;

/// <summary>Historical user intent. Navigation never consults this record.</summary>
public sealed record ServiceApproval(AccessProposal Proposal, Guid VaultProposalId, DateTimeOffset ApprovedAt, long AppliedPolicyRevision)
{
    public void Validate()
    {
        if (Proposal is null || !Proposal.CanStage || VaultProposalId == Guid.Empty || ApprovedAt < DateTimeOffset.UnixEpoch ||
            AppliedPolicyRevision != Proposal.PolicyRevision + 1)
            throw new InvalidOperationException("Invalid service approval provenance.");
    }
}
