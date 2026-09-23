using Zenith.Core.Registry;

namespace Zenith.App.Registry;

internal sealed class OptionalServiceScope(ProposedDomain domain, bool selected)
{
    public string Hostname => domain.Hostname;
    public string Label => $"{domain.Hostname} — {string.Join("; ", domain.Reasons)} ({ServiceProposalPresentation.State(domain.AccessState)})";
    public bool IsSelected { get; set; } = selected;
}

internal static class ServiceProposalPresentation
{
    internal static string State(ProposalAccessState state) => state switch
    { ProposalAccessState.NewAccess => "New access", ProposalAccessState.AlreadyAllowed => "Already available", _ => "Blocked by Blacklist" };
    internal static string Summary(AccessProposal proposal) => $"Add {proposal.ServiceName} to Sphere\n\n" +
        string.Join("\n\n", proposal.ProposedDomains.Select(d =>
            $"{d.Hostname} — {(d.Required ? "Required" : "Selected optional")}, {State(d.AccessState)}\n{string.Join("; ", d.Reasons)}")) +
        "\n\n" + string.Join("\n", proposal.Conflicts.Concat(proposal.Warnings)) +
        "\nExisting hostname access does not imply previous service approval. No change takes effect until Vault confirmation.";

    internal static string Details(AccessProposal proposal) =>
        $"Service: {proposal.SelectedService.ServiceId}\nRegistry: {proposal.RegistryRevision}\nCatalog fingerprint: {proposal.RegistryFingerprint}\nProposal identity: {proposal.ProposalId}\nPolicy revision: {proposal.PolicyRevision}\n" +
        string.Join("\n\n", proposal.ProposedDomains.Concat(proposal.OptionalDomains).Select(d =>
            $"{d.Hostname}: exact hostname only\nExisting coverage: {string.Join(", ", d.ExistingScopes.Select(s => s.Hostname + (s.IncludeSubdomains ? " (including subdomains)" : " (exact)")))}\n" +
            string.Join("\n", d.Relationships.Select(r =>
                $"Service: {r.ServiceId}; infrastructure: {r.InfrastructureId ?? "none"}\nPurpose: {r.Requirement.Purpose}\nDependency: {r.Requirement.DependencyType}; applicability: {r.Requirement.PermissionApplicability}\n" +
                RegistryLabels.Evidence(r.Requirement.Provenance) + "\nInfrastructure relationship: " + RegistryLabels.Evidence(r.InfrastructureProvenance)))));
}
