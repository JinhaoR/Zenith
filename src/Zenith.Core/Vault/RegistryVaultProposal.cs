using Zenith.Core.Navigation;
using Zenith.Core.Registry;

namespace Zenith.Core.Vault;

/// <summary>Independent registry/local proposals use ordinary Vault edits and the same transaction.</summary>
public static class RegistryVaultProposal
{
    public static VaultEdit CreateEdit(InfrastructureBaselineProposal proposal) => new(
        AddSites: Array.AsReadOnly(proposal.NewHostnames.Select(h => new VaultSiteAddition(h)).ToArray()), RemoveSites: [],
        InfrastructureProposal: proposal);

    public static VaultEdit CreateEdit(LocalServiceExtensionProposal proposal, SitePolicySnapshot policy)
    {
        if (proposal.PolicyRevision != policy.Revision) throw new ArgumentException("Policy changed. Review the local exception again.");
        var access = policy.Classify(new SitePolicyEntry(proposal.Hostname, AccessClass.Whitelist).Identity);
        if (access == AccessClass.Blacklist) throw new ArgumentException("Blacklist prevents this local exception.");
        var additions = access == AccessClass.Whitelist ? Array.Empty<VaultSiteAddition>() : [new VaultSiteAddition(proposal.Hostname)];
        return new(AddSites: Array.AsReadOnly(additions), RemoveSites: [], LocalExtensionProposal: proposal);
    }

    internal static VaultEdit Normalize(VaultState state, VaultEdit edit)
    {
        VaultEdit expected;
        IEnumerable<string> hosts;
        long revision;
        if (edit.InfrastructureProposal is { } baseline)
        {
            expected = CreateEdit(baseline);
            hosts = baseline.Hostnames;
            revision = baseline.PolicyRevision;
            if (state.InfrastructureActivations.Count >= 1000 || state.InfrastructureActivations.Any(a => a.Proposal.ProposalId == baseline.ProposalId))
                throw new ArgumentException("This baseline approval already exists or its history limit has been reached.");
        }
        else if (edit.LocalExtensionProposal is { } local)
        {
            if (state.ToPolicy().Classify(new SitePolicyEntry(local.ServiceEntryPointHostname, AccessClass.Whitelist).Identity) != AccessClass.Whitelist)
                throw new ArgumentException("The service context is no longer in your Sphere.");
            expected = CreateEdit(local, state.ToPolicy());
            hosts = [local.Hostname];
            revision = local.PolicyRevision;
            if (state.LocalServiceExtensions.Count >= 1000 || state.GetActiveLocalExtensions().Any(e =>
                    e.Proposal.Hostname == local.Hostname && e.Proposal.ServiceId == local.ServiceId))
                throw new ArgumentException("This local exception already exists or its history limit has been reached.");
            var covered = state.ToPolicy().Classify(new SitePolicyEntry(local.Hostname, AccessClass.Whitelist).Identity) == AccessClass.Whitelist;
            if (covered && !state.Sites.Any(s => s.Host == local.Hostname && s.AccessClass == AccessClass.Whitelist && !s.IncludeSubdomains))
                throw new ArgumentException("A broader permission already covers this endpoint. Review that scope in Vault; an exact local exception cannot restrict it.");
        }
        else throw new ArgumentException("Missing registry proposal.");

        if (revision != state.Revision || edit.ServiceProposal is not null ||
            edit.GreylistSeconds is not null || edit.GrantSeconds is not null || edit.VaultSeconds is not null ||
            edit.ChangePassword || edit.DisablePassword || edit.AddHost is not null || edit.RemoveHost is not null || edit.IncludeSubdomains ||
            edit.AddSites is null || edit.RemoveSites is null || edit.RemoveSites.Count != 0 || !edit.AddSites.SequenceEqual(expected.AddSites!))
            throw new ArgumentException("Review this proposal again. It must preserve its exact scope and current policy revision.");

        var policy = state.ToPolicy();
        foreach (var host in hosts)
        {
            var access = policy.Classify(new SitePolicyEntry(host, AccessClass.Whitelist).Identity);
            if (access == AccessClass.Blacklist || (access != AccessClass.Whitelist) != expected.Additions().Any(a => a.Host == host))
                throw new ArgumentException("Proposed access conflicts with current policy.");
        }
        if (expected.AddSites!.Count > 0)
            _ = VaultProposalRules.Normalize(state, expected with { InfrastructureProposal = null, LocalExtensionProposal = null });
        return expected;
    }

    internal static VaultState RecordConfirmation(VaultState state, VaultEdit edit, Guid operationId, DateTimeOffset now)
    {
        if (edit.InfrastructureProposal is { } baseline)
            state = state with { InfrastructureActivations = Array.AsReadOnly(state.InfrastructureActivations.Append(
                new InfrastructureActivation(baseline, operationId, now, state.Revision,
                    baseline.NewHostnames.Select(h => state.Sites.Single(s => s.Host == h && s.AccessClass == AccessClass.Whitelist).PermissionInstanceId).ToArray())).ToArray()) };
        if (edit.LocalExtensionProposal is { } local)
            state = state with { LocalServiceExtensions = Array.AsReadOnly(state.LocalServiceExtensions.Append(
                new LocalServiceExtension(local, operationId, now, state.Revision,
                    state.Sites.Single(s => s.Host == local.Hostname && s.AccessClass == AccessClass.Whitelist && !s.IncludeSubdomains).PermissionInstanceId)).ToArray()) };
        return state;
    }

    internal static void ValidateHistory(VaultState state)
    {
        if (state.InfrastructureActivations is null || state.LocalServiceExtensions is null ||
            state.InfrastructureActivations.Count > 1000 || state.LocalServiceExtensions.Count > 1000)
            throw new InvalidOperationException("Invalid local registry history.");
        var operations = state.ServiceApprovals.Select(a => a.VaultProposalId).ToHashSet();
        var permissions = state.PermissionInstances.ToDictionary(p => p.Id);
        foreach (var activation in state.InfrastructureActivations)
        {
            if (activation?.Proposal is not { } proposal) throw new InvalidOperationException("Missing infrastructure approval.");
            ValidateOperation(activation.VaultOperationId, activation.AppliedPolicyRevision, proposal.PolicyRevision, activation.ApprovedAt);
            if (activation.CreatedPermissionIds.Distinct().Count() != activation.CreatedPermissionIds.Count ||
                activation.CreatedPermissionIds.Count != proposal.NewHostnames.Count)
                throw new InvalidOperationException("Invalid infrastructure creation metadata.");
            var createdHosts = activation.CreatedPermissionIds.Select(id =>
            {
                if (!permissions.TryGetValue(id, out var p) || p.AccessClass != AccessClass.Whitelist || p.IncludeSubdomains ||
                    p.CreatedPolicyRevision != activation.AppliedPolicyRevision)
                    throw new InvalidOperationException("Infrastructure history references an invalid permission.");
                return p.Host;
            }).Order(StringComparer.Ordinal);
            if (!createdHosts.SequenceEqual(proposal.NewHostnames.Order(StringComparer.Ordinal)))
                throw new InvalidOperationException("Infrastructure permission scopes changed.");
        }
        foreach (var extension in state.LocalServiceExtensions)
        {
            if (extension?.Proposal is not { } proposal) throw new InvalidOperationException("Missing local exception approval.");
            ValidateOperation(extension.VaultOperationId, extension.AppliedPolicyRevision, proposal.PolicyRevision, extension.ApprovedAt);
            if (!permissions.TryGetValue(extension.PermissionInstanceId, out var p) || p.Host != proposal.Hostname ||
                p.AccessClass != AccessClass.Whitelist || p.IncludeSubdomains || p.CreatedPolicyRevision > extension.AppliedPolicyRevision)
                throw new InvalidOperationException("Local exception references an invalid permission.");
        }
        void ValidateOperation(Guid id, long revision, long baseRevision, DateTimeOffset approvedAt)
        {
            if (id == Guid.Empty || !operations.Add(id) || revision <= 0 || revision > state.Revision ||
                baseRevision != revision - 1 || approvedAt < DateTimeOffset.UnixEpoch || approvedAt > state.LastObservedUtc)
                throw new InvalidOperationException("Invalid registry approval operation.");
        }
    }
}
