using Zenith.Core.Navigation;
using Zenith.Core.Registry;

namespace Zenith.Core.Vault;

/// <summary>Adapts frozen Core consequences to an ordinary Vault edit; never stages or applies it.</summary>
public static class ServiceVaultProposal
{
    public static VaultEdit CreateEdit(AccessProposal proposal)
    {
        if (!proposal.CanStage) throw new ArgumentException("This service proposal cannot be staged. Review its conflicts.");
        return new(AddSites: Array.AsReadOnly(proposal.ProposedDomains.Where(d => d.AccessState == ProposalAccessState.NewAccess)
            .Select(d => new VaultSiteAddition(d.Hostname)).ToArray()), RemoveSites: [], ServiceProposal: proposal);
    }

    internal static VaultEdit Normalize(VaultState state, VaultEdit edit)
    {
        var proposal = edit.ServiceProposal!;
        if (!proposal.CanStage || proposal.PolicyRevision != state.Revision ||
            state.ServiceApprovals.Any(a => a.Proposal.ProposalId == proposal.ProposalId))
            throw new ArgumentException("Service proposal is invalid, already approved or based on outdated policy. Review it again.");
        var expected = CreateEdit(proposal);
        if (edit.GreylistSeconds is not null || edit.GrantSeconds is not null || edit.VaultSeconds is not null ||
            edit.ChangePassword || edit.DisablePassword || edit.AddHost is not null || edit.RemoveHost is not null || edit.IncludeSubdomains ||
            edit.AddSites is null || edit.RemoveSites is null || edit.RemoveSites.Count != 0 || !edit.AddSites.SequenceEqual(expected.AddSites!))
            throw new ArgumentException("A service proposal must retain its exact frozen hostname additions.");
        var policy = state.ToPolicy();
        foreach (var d in proposal.ProposedDomains)
        {
            var identity = new SitePolicyEntry(d.Hostname, AccessClass.Whitelist).Identity;
            var current = policy.Classify(identity);
            if (current == AccessClass.Blacklist || (current == AccessClass.Whitelist) != (d.AccessState == ProposalAccessState.AlreadyAllowed))
                throw new ArgumentException("A proposed service scope conflicts with current policy.");
        }
        // Ordinary hostname validation still handles additions and size limits. Attribution-only
        // approval deliberately uses the same wait/confirmation even when no rule needs adding.
        if (expected.AddSites!.Count > 0) _ = VaultProposalRules.Normalize(state, expected with { ServiceProposal = null });
        if (state.ServiceApprovals.Count >= 1000) throw new ArgumentException("The service approval limit has been reached.");
        return expected;
    }
}
