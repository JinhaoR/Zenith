using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

public sealed record ReviewedInfrastructureEndpoint(string InfrastructureId, string Name, DomainRequirement Requirement);

/// <summary>A frozen exact-host baseline for Vault review, never a live allowlist.</summary>
public sealed record InfrastructureBaselineProposal
{
    public InfrastructureBaselineProposal(string registryRevision, string registryFingerprint, long policyRevision,
        IReadOnlyList<ReviewedInfrastructureEndpoint> endpoints, IReadOnlyList<string> newHostnames)
    {
        RegistryRevision = RegistryFields.Text(registryRevision, nameof(RegistryRevision), 80);
        if (!RegistryContentIdentity.IsFingerprint(registryFingerprint) || policyRevision < 0)
            throw new ArgumentException("Invalid infrastructure proposal identity.");
        RegistryFingerprint = registryFingerprint;
        PolicyRevision = policyRevision;
        Endpoints = RegistryFields.Copy(endpoints, nameof(Endpoints));
        if (Endpoints.Count == 0) throw new ArgumentException("No reviewed infrastructure navigation endpoints are available.");
        foreach (var endpoint in Endpoints)
        {
            RegistryFields.Id(endpoint.InfrastructureId);
            RegistryFields.Text(endpoint.Name, nameof(endpoint.Name), 200);
            if (endpoint.Requirement is not { PermissionApplicability: PermissionApplicability.RequiredNavigation } domain ||
                !ServiceAccessProposalBuilder.HasReview(domain.Provenance))
                throw new ArgumentException("Infrastructure baseline endpoints require recorded navigation review.");
        }
        if (Endpoints.Select(e => (e.InfrastructureId, e.Requirement.Hostname)).Distinct().Count() != Endpoints.Count)
            throw new ArgumentException("Duplicate infrastructure endpoint.");
        NewHostnames = RegistryFields.Copy(newHostnames, nameof(NewHostnames));
        if (NewHostnames.Distinct(StringComparer.Ordinal).Count() != NewHostnames.Count ||
            NewHostnames.Any(h => !Hostnames.Contains(h, StringComparer.Ordinal)))
            throw new ArgumentException("Invalid infrastructure hostname additions.");
    }

    public string RegistryRevision { get; }
    public string RegistryFingerprint { get; }
    public long PolicyRevision { get; }
    public IReadOnlyList<ReviewedInfrastructureEndpoint> Endpoints { get; }
    public IReadOnlyList<string> NewHostnames { get; }
    public IReadOnlyList<string> Hostnames => Array.AsReadOnly(Endpoints.Select(e => e.Requirement.Hostname)
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    public string ProposalId => RegistryContentIdentity.InfrastructureProposal(this);

    public static InfrastructureBaselineProposal Prepare(ServiceRegistry registry, SitePolicySnapshot policy)
    {
        var endpoints = registry.Infrastructure.OrderBy(i => i.Id, StringComparer.Ordinal)
            .SelectMany(i => i.Domains.Where(d => d.PermissionApplicability == PermissionApplicability.RequiredNavigation)
                .OrderBy(d => d.Hostname, StringComparer.Ordinal).Select(d => new ReviewedInfrastructureEndpoint(i.Id, i.Name, d))).ToArray();
        var hosts = endpoints.Select(e => e.Requirement.Hostname).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (hosts.Any(h => policy.Classify(new SitePolicyEntry(h, AccessClass.Whitelist).Identity) == AccessClass.Blacklist))
            throw new ArgumentException("Blacklist prevents activation of this infrastructure baseline.");
        return new(registry.Revision, RegistryContentIdentity.Registry(registry), policy.Revision, endpoints,
            hosts.Where(h => policy.Classify(new SitePolicyEntry(h, AccessClass.Whitelist).Identity) != AccessClass.Whitelist).ToArray());
    }
}
