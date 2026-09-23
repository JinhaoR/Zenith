using Zenith.Core.Navigation;

namespace Zenith.Core.Registry;

public enum ProposalAccessState { NewAccess, AlreadyAllowed, Blacklisted }
public sealed record ExistingHostScope(string Hostname, bool IncludeSubdomains);
public sealed record ProposalRelationship(string ServiceId, string? InfrastructureId, DomainRequirement Requirement,
    RelationshipProvenance? InfrastructureProvenance);

/// <summary>An exact hostname candidate with explanations; it has no permission effect.</summary>
public sealed record ProposedDomain
{
    public ProposedDomain(string hostname, IReadOnlyList<string> reasons,
        IReadOnlyList<ProposalRelationship>? relationships = null, ProposalAccessState accessState = ProposalAccessState.NewAccess,
        IReadOnlyList<ExistingHostScope>? existingScopes = null)
    {
        if (!SiteIdentity.TryCreate(hostname, out var identity))
            throw new ArgumentException("A proposed domain must be a hostname without a URL, path, wildcard or port.", nameof(hostname));
        Hostname = identity.Host;
        Reasons = RegistryFields.Copy(reasons, nameof(Reasons));
        if (Reasons.Count == 0) throw new ArgumentException("A proposed domain needs a reason.", nameof(reasons));
        foreach (var reason in Reasons) RegistryFields.Text(reason, nameof(Reasons));
        Relationships = RegistryFields.Copy(relationships ?? [], nameof(Relationships));
        if (!Enum.IsDefined(accessState)) throw new ArgumentException("Invalid proposed access state.");
        AccessState = accessState;
        ExistingScopes = RegistryFields.Copy(existingScopes ?? [], nameof(ExistingScopes));
    }

    public string Hostname { get; }
    public IReadOnlyList<string> Reasons { get; }
    public IReadOnlyList<ProposalRelationship> Relationships { get; }
    public ProposalAccessState AccessState { get; }
    public IReadOnlyList<ExistingHostScope> ExistingScopes { get; }
    public bool Required => Relationships.Any(r => r.Requirement.Required);
    public bool IncludeSubdomains => false;
}

/// <summary>
/// Frozen proposed consequences or a legacy descriptive container, never authorization.
/// Construction validates evidence shape without resolving a live registry or policy.
/// Staging, persistence and approval belong to the separate Vault workflow.
/// </summary>
public sealed record AccessProposal
{
    public AccessProposal(ServiceSelection selectedService, IReadOnlyList<ProposedDomain> proposedDomains,
        string? registryFingerprint = null, long? policyRevision = null, string? serviceName = null,
        IReadOnlyList<ProposedDomain>? optionalDomains = null, IReadOnlyList<string>? conflicts = null,
        IReadOnlyList<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(selectedService);
        SelectedService = selectedService;
        ProposedDomains = RegistryFields.Copy(proposedDomains, nameof(ProposedDomains));
        if (ProposedDomains.Select(d => d.Hostname).Distinct(StringComparer.Ordinal).Count() != ProposedDomains.Count)
            throw new ArgumentException("A proposal cannot contain duplicate normalized hostnames.", nameof(proposedDomains));
        RegistryFingerprint = registryFingerprint;
        PolicyRevision = policyRevision;
        ServiceName = serviceName;
        OptionalDomains = RegistryFields.Copy(optionalDomains ?? [], nameof(OptionalDomains));
        Conflicts = RegistryFields.Copy(conflicts ?? [], nameof(Conflicts));
        Warnings = RegistryFields.Copy(warnings ?? [], nameof(Warnings));
        foreach (var message in Conflicts.Concat(Warnings)) RegistryFields.Text(message, "Proposal message");
        if (registryFingerprint is not null)
        {
            if (!RegistryContentIdentity.IsFingerprint(registryFingerprint) || policyRevision is null or < 0)
                throw new ArgumentException("Invalid frozen proposal identity.");
            RegistryFields.Text(serviceName!, nameof(ServiceName), 200);
            ValidateRelationships();
        }
    }

    public ServiceSelection SelectedService { get; }
    public IReadOnlyList<ProposedDomain> ProposedDomains { get; }
    public string RegistryRevision => SelectedService.RegistryRevision;
    public string? RegistryFingerprint { get; }
    public long? PolicyRevision { get; }
    public string? ServiceName { get; }
    public IReadOnlyList<ProposedDomain> OptionalDomains { get; }
    public IReadOnlyList<string> Conflicts { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool IsFrozen => RegistryFingerprint is not null;
    public string? ProposalId => IsFrozen ? RegistryContentIdentity.Proposal(this) : null;
    public bool CanStage => IsFrozen && ProposedDomains.Count > 0 && Conflicts.Count == 0 &&
        ProposedDomains.All(d => d.AccessState != ProposalAccessState.Blacklisted);

    private void ValidateRelationships()
    {
        var all = ProposedDomains.Concat(OptionalDomains).ToArray();
        if (all.Select(d => d.Hostname).Distinct().Count() != all.Length) throw new ArgumentException("Duplicate proposal candidates.");
        foreach (var domain in all)
        {
            if (domain.Relationships.Count == 0 || domain.Relationships.Any(r => r is null || r.ServiceId != SelectedService.ServiceId ||
                r.Requirement.Hostname != domain.Hostname || !ServiceAccessProposalBuilder.IsEligible(r)))
                throw new ArgumentException("Proposal contains an unreviewed relationship.");
            if ((domain.AccessState == ProposalAccessState.AlreadyAllowed) != (domain.ExistingScopes.Count > 0) ||
                domain.ExistingScopes.Any(s => !SiteIdentity.TryCreate(s.Hostname, out var id) || id.Host != s.Hostname ||
                    !new SitePolicyEntry(s.Hostname, AccessClass.Whitelist, s.IncludeSubdomains).Matches(new SitePolicyEntry(domain.Hostname, AccessClass.Whitelist).Identity)))
                throw new ArgumentException("Invalid existing access attribution.");
        }
        if (OptionalDomains.Any(d => d.Required) ||
            !ProposedDomains.Where(d => !d.Required).Select(d => d.Hostname).Order(StringComparer.Ordinal).SequenceEqual(SelectedService.OptionalHostnames))
            throw new ArgumentException("Optional selections do not match the frozen proposal.");
    }
}
