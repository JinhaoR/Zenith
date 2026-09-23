using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

/// <summary>Pure preparation. Only Vault can approve these frozen consequences.</summary>
public sealed class ServiceAccessProposalBuilder
{
    public static bool HasReview(RelationshipProvenance? p) => p is { Review: not null, SourceReference: not null, Explanation: not null };
    public static bool IsEligible(ProposalRelationship r) =>
        r.Requirement.PermissionApplicability is PermissionApplicability.RequiredNavigation or PermissionApplicability.OptionalNavigation &&
        HasReview(r.Requirement.Provenance) && (r.InfrastructureId is null || HasReview(r.InfrastructureProvenance));
    public bool CanOffer(ServiceRegistry registry, string serviceId) => registry.FindService(serviceId)?.EntryPoints
        .Any(d => IsEligible(new(serviceId, null, d, null))) == true;

    public AccessProposal Build(ServiceSelection selection, ServiceRegistry registry, SitePolicySnapshot policy)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(policy);
        if (selection.RegistryRevision != registry.Revision) throw new ArgumentException("The selected registry revision is unavailable. Review the service again.");
        var service = registry.FindService(selection.ServiceId) ?? throw new ArgumentException("The service is unavailable.");
        var relationships = service.EntryPoints.Select(d =>
            new ProposalRelationship(service.Id, null, d, null)).ToArray();
        var conflicts = new List<string>();
        var warnings = new List<string> { "Only reviewed service entry points are proposed. Shared infrastructure needs separate Vault activation; compatibility is not guaranteed." };
        foreach (var r in relationships.Where(r => !IsEligible(r)).OrderBy(r => r.Requirement.Hostname, StringComparer.Ordinal).ThenBy(r => r.InfrastructureId, StringComparer.Ordinal))
        {
            warnings.Add($"{r.Requirement.Hostname}: descriptive or background-only relationship; no navigation permission proposed.");
            if (r.Requirement.Required && r.Requirement.PermissionApplicability != PermissionApplicability.BackgroundOnly)
                conflicts.Add($"Required relationship {r.Requirement.Hostname} has not been reviewed for navigation. The service may be incomplete.");
        }
        var candidates = relationships.Where(IsEligible).GroupBy(r => r.Requirement.Hostname)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            {
                var identity = new SitePolicyEntry(g.Key, AccessClass.Whitelist).Identity;
                var state = policy.Classify(identity) switch
                { AccessClass.Whitelist => ProposalAccessState.AlreadyAllowed, AccessClass.Blacklist => ProposalAccessState.Blacklisted, _ => ProposalAccessState.NewAccess };
                var scopes = state == ProposalAccessState.AlreadyAllowed ? policy.Entries.Where(e => e.AccessClass == AccessClass.Whitelist && e.Matches(identity))
                    .OrderBy(e => e.Identity.Host, StringComparer.Ordinal).ThenBy(e => e.IncludeSubdomains)
                    .Select(e => new ExistingHostScope(e.Identity.Host, e.IncludeSubdomains)).Distinct().ToArray() : [];
                var edges = g.OrderBy(r => r.InfrastructureId, StringComparer.Ordinal).ToArray();
                return new ProposedDomain(g.Key, edges.Select(r => r.Requirement.Purpose).Distinct().ToArray(), edges, state, scopes);
            }).ToArray();
        foreach (var host in selection.OptionalHostnames)
            if (!candidates.Any(d => !d.Required && d.Hostname == host))
                throw new ArgumentException("An optional selection is not an eligible optional navigation requirement.");
        var included = candidates.Where(d => d.Required || selection.OptionalHostnames.Contains(d.Hostname)).ToArray();
        foreach (var d in included.Where(d => d.AccessState == ProposalAccessState.Blacklisted))
            conflicts.Add($"{d.Hostname}: Blacklist prevents this access. The service may not function completely.");
        if (included.Length == 0) conflicts.Add("No reviewed navigation access was selected.");
        return new(selection, included, RegistryContentIdentity.Registry(registry), policy.Revision, service.Name,
            candidates.Where(d => !d.Required && !selection.OptionalHostnames.Contains(d.Hostname)).ToArray(), conflicts, warnings);
    }
}
