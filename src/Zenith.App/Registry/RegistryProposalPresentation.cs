using Zenith.Core.Vault;

namespace Zenith.App.Registry;

internal static class RegistryProposalPresentation
{
    internal static string Summary(VaultEdit edit) => edit.InfrastructureProposal is { } baseline
        ? "Activate reviewed shared infrastructure\n\n" + string.Join("\n", baseline.Hostnames.Select(h =>
            $"{h} — {(baseline.NewHostnames.Contains(h) ? "New exact-host access" : "Already available; existing scope unchanged")}")) +
          "\n\nThis permits profile-wide navigation, including direct visits. Every redirect still follows Core policy. No catalog update can expand this approval."
        : edit.LocalExtensionProposal is { } local
            ? $"Local exception for {local.ServiceName}\n\n{local.Hostname}\n{local.Purpose}\n\nUser-approved local exception, not Zenith-reviewed infrastructure. Exact-host access applies across this profile; it is not restricted to this service."
            : string.Empty;

    internal static string Details(VaultEdit edit) => edit.InfrastructureProposal is { } baseline
        ? $"Registry: {baseline.RegistryRevision}\nProposal: {baseline.ProposalId}\nPolicy revision: {baseline.PolicyRevision}\n" +
          string.Join("\n\n", baseline.Endpoints.Select(e => $"{e.Name}: {e.Requirement.Hostname}\n{e.Requirement.Purpose}\n{RegistryLabels.Evidence(e.Requirement.Provenance)}"))
        : edit.LocalExtensionProposal is { } local
            ? $"Service label: {local.ServiceId}\nHost: {local.Hostname} (exact)\nSource: User-approved local exception\nProposal: {local.ProposalId}\nPolicy revision: {local.PolicyRevision}\nStored only on this device. Remove access using Vault's hostname removal workflow; approval history remains."
            : string.Empty;
}
