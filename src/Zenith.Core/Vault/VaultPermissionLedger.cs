using Zenith.Core.Navigation;
using Zenith.Core.Registry;

namespace Zenith.Core.Vault;

/// <summary>Records identity and evidence alongside policy. It never derives policy from that evidence.</summary>
public static class VaultPermissionLedger
{
    public const int MaximumRecords = 10000;

    // Only the versioned storage migration calls this in production. Existing approval
    // history is retained, but hostname overlap never manufactures current bindings.
    public static VaultState MigrateLegacy(VaultState state) => Initialize(state, PermissionAttributionSource.LegacyMigration);

    internal static VaultState Initialize(VaultState state, PermissionAttributionSource source)
    {
        state.ValidateLegacy();
        var sites = state.Sites.Select(s => s with { PermissionInstanceId = Guid.NewGuid() }).ToArray();
        var result = state with
        {
            Sites = sites,
            PermissionInstances = Array.AsReadOnly(sites.Select(s => Snapshot(s, state.Revision)).ToArray()),
            PermissionAttributions = Array.AsReadOnly(sites.Select(s => new PermissionAttribution(
                s.PermissionInstanceId, source, state.Revision)).ToArray())
        };
        result.Validate();
        return result;
    }

    internal static VaultState RecordConfirmation(VaultState state, VaultEdit edit, Guid operationId)
    {
        var instances = state.PermissionInstances.ToList();
        var attributions = state.PermissionAttributions.ToList();
        var sites = state.Sites.Select(s => s.PermissionInstanceId == Guid.Empty
            ? s with { PermissionInstanceId = Guid.NewGuid() } : s).ToArray();
        var known = instances.Select(p => p.Id).ToHashSet();
        foreach (var site in sites)
            if (known.Add(site.PermissionInstanceId)) instances.Add(Snapshot(site, state.Revision));

        if (edit.ServiceProposal is { } proposal)
        {
            foreach (var domain in proposal.ProposedDomains)
            {
                // Bind to precisely the coverage frozen by review, not arbitrary later
                // permissions with a matching hostname. Vault has checked the base
                // revision and holds the transaction lock through this confirmation.
                var scopes = domain.AccessState == ProposalAccessState.NewAccess
                    ? new[] { new ExistingHostScope(domain.Hostname, false) }
                    : domain.ExistingScopes;
                foreach (var scope in scopes)
                {
                    var site = sites.Single(s => s.AccessClass == AccessClass.Whitelist &&
                        s.Host == scope.Hostname && s.IncludeSubdomains == scope.IncludeSubdomains);
                    for (var i = 0; i < domain.Relationships.Count; i++)
                    {
                        attributions.Add(new(site.PermissionInstanceId, PermissionAttributionSource.ServiceApproval,
                            state.Revision, operationId, operationId, new(domain.Hostname, i)));
                        CheckLimit(attributions.Count);
                    }
                }
            }
        }
        else
        {
            // A later parent addition can consolidate an earlier child in the same
            // batch. Only final, committed rules acquire identity and attribution.
            var addedHosts = edit.Additions().Select(a => a.Host).ToHashSet(StringComparer.Ordinal);
            foreach (var site in sites.Where(s => s.AccessClass == AccessClass.Whitelist && addedHosts.Contains(s.Host)))
            {
                attributions.Add(new(site.PermissionInstanceId, PermissionAttributionSource.ManualVaultChange,
                    state.Revision, operationId));
            }
        }
        CheckLimit(instances.Count);
        CheckLimit(attributions.Count);
        var result = state with { Sites = sites, PermissionInstances = instances.AsReadOnly(), PermissionAttributions = attributions.AsReadOnly() };
        result.Validate();
        return result;
    }

    internal static void Validate(VaultState state)
    {
        if (state.PermissionInstances is null || state.PermissionAttributions is null)
            throw new InvalidOperationException("Missing permission identity or attribution.");
        CheckLimit(state.PermissionInstances.Count);
        CheckLimit(state.PermissionAttributions.Count);
        var instances = new Dictionary<Guid, PermissionInstance>();
        foreach (var instance in state.PermissionInstances)
        {
            if (instance is null || instance.Id == Guid.Empty || !instances.TryAdd(instance.Id, instance) ||
                instance.CreatedPolicyRevision < 0 || instance.CreatedPolicyRevision > state.Revision ||
                !SiteIdentity.TryCreate(instance.Host, out var identity) || identity.Host != instance.Host)
                throw new InvalidOperationException("Invalid permission instance.");
            _ = new SitePolicyEntry(instance.Host, instance.AccessClass, instance.IncludeSubdomains);
        }
        if (state.Sites.Select(s => s.PermissionInstanceId).Distinct().Count() != state.Sites.Length ||
            state.Sites.Any(s => !instances.TryGetValue(s.PermissionInstanceId, out var instance) ||
                instance.Host != s.Host || instance.AccessClass != s.AccessClass || instance.IncludeSubdomains != s.IncludeSubdomains))
            throw new InvalidOperationException("Active policy does not match its permission instances.");

        var approvals = state.ServiceApprovals.ToDictionary(a => a.VaultProposalId);
        var unique = new HashSet<PermissionAttribution>();
        var attributed = new HashSet<Guid>();
        foreach (var a in state.PermissionAttributions)
        {
            if (a is null || !unique.Add(a) || !instances.TryGetValue(a.PermissionInstanceId, out var instance) ||
                !Enum.IsDefined(a.Source) || a.RecordedPolicyRevision < instance.CreatedPolicyRevision || a.RecordedPolicyRevision > state.Revision)
                throw new InvalidOperationException("Invalid permission attribution.");
            attributed.Add(a.PermissionInstanceId);
            switch (a.Source)
            {
                case PermissionAttributionSource.InitialPolicy:
                case PermissionAttributionSource.LegacyMigration:
                    if (a.OriginatingVaultOperationId is not null || a.ServiceApprovalId is not null || a.Requirement is not null)
                        throw new InvalidOperationException("Legacy or initial attribution cannot invent a Vault operation.");
                    break;
                case PermissionAttributionSource.ManualVaultChange:
                    if (a.OriginatingVaultOperationId is null || a.OriginatingVaultOperationId == Guid.Empty ||
                        a.RecordedPolicyRevision == 0 || a.ServiceApprovalId is not null || a.Requirement is not null ||
                        instance.AccessClass != AccessClass.Whitelist)
                        throw new InvalidOperationException("Invalid manual permission attribution.");
                    break;
                case PermissionAttributionSource.ServiceApproval:
                    if (a.ServiceApprovalId is not { } approvalId || !approvals.TryGetValue(approvalId, out var approval) ||
                        a.OriginatingVaultOperationId != approvalId || a.RecordedPolicyRevision != approval.AppliedPolicyRevision ||
                        a.Requirement is not { } requirement || instance.AccessClass != AccessClass.Whitelist)
                        throw new InvalidOperationException("Invalid service permission attribution.");
                    var domain = approval.Proposal.ProposedDomains.SingleOrDefault(d => d.Hostname == requirement.Hostname);
                    if (domain is null || requirement.RelationshipIndex < 0 || requirement.RelationshipIndex >= domain.Relationships.Count ||
                        !new SitePolicyEntry(instance.Host, instance.AccessClass, instance.IncludeSubdomains)
                            .Matches(new SitePolicyEntry(domain.Hostname, AccessClass.Whitelist).Identity) ||
                        (domain.AccessState == ProposalAccessState.NewAccess
                            ? instance.Host != domain.Hostname || instance.IncludeSubdomains || instance.CreatedPolicyRevision != a.RecordedPolicyRevision
                            : instance.CreatedPolicyRevision >= a.RecordedPolicyRevision ||
                                !domain.ExistingScopes.Contains(new ExistingHostScope(instance.Host, instance.IncludeSubdomains))))
                        throw new InvalidOperationException("Attribution does not match the approved requirement and scope.");
                    break;
            }
        }
        if (instances.Keys.Any(id => !attributed.Contains(id)))
            throw new InvalidOperationException("Permission instance has no recorded source.");
    }

    private static PermissionInstance Snapshot(VaultSite site, long revision) =>
        new(site.PermissionInstanceId, site.Host, site.AccessClass, site.IncludeSubdomains, revision);
    private static void CheckLimit(int count)
    {
        if (count > MaximumRecords) throw new InvalidOperationException("Permission history exceeds its record limit.");
    }
}
