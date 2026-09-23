using System.Windows;
using Zenith.App.Registry;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.App.Settings;

public partial class VaultPanel
{
    private ServiceRegistry? _registry;
    private bool _loadingRegistry;

    private static bool IsFrozenRegistryEdit(VaultEdit edit) => edit.ServiceProposal is not null ||
        edit.InfrastructureProposal is not null || edit.LocalExtensionProposal is not null;

    private async void RegistryOptions_OnExpanded(object sender, RoutedEventArgs e) => await LoadRegistryOptionsAsync();
    private async void ReloadRegistry_OnClick(object sender, RoutedEventArgs e) => await LoadRegistryOptionsAsync();

    internal async Task LoadRegistryOptionsAsync()
    {
        if (_loadingRegistry) return;
        _loadingRegistry = true;
        InfrastructureReviewButton.IsEnabled = LocalExtensionReviewButton.IsEnabled = false;
        _registry = null;
        RegistryStatus.Text = "Loading the local catalog…";
        try
        {
            var result = await new JsonServiceRegistryLoader().LoadBundledAsync();
            if (_detached) return;
            _registry = result.Registry;
            LocalExtensionService.ItemsSource = _registry?.Services.OrderBy(s => s.Name).ToArray() ?? [];
            InfrastructureReviewButton.IsEnabled = LocalExtensionReviewButton.IsEnabled = _registry is not null;
            RegistryStatus.Text = _registry is null ? "The catalog is unavailable. Existing permissions and Vault changes are unaffected." :
                $"Catalog {_registry.Revision}. Loading it grants no access. Each review below follows the full Vault workflow.";
        }
        catch (Exception) { RegistryStatus.Text = "The catalog could not be loaded. Existing permissions are unchanged."; }
        finally { _loadingRegistry = false; }
    }

    private void ReviewInfrastructure_OnClick(object sender, RoutedEventArgs e)
    {
        InvalidateReview();
        try
        {
            if (_registry is null || _service is null || !_service.TryGetActivePolicy(out var policy))
                throw new InvalidOperationException("Catalog or policy unavailable.");
            ReviewFrozenProposal(RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(_registry, policy)));
        }
        catch (ArgumentException error) { ShowValidationError(error.Message); }
        catch (Exception) { ShowValidationError("Infrastructure could not be reviewed safely. No change was made."); }
    }

    private void ReviewLocalExtension_OnClick(object sender, RoutedEventArgs e)
    {
        InvalidateReview();
        try
        {
            if (_registry is null || LocalExtensionService.SelectedItem is not ServiceDefinition service ||
                _service is null || !_service.TryGetActivePolicy(out var policy))
                throw new ArgumentException("Choose a service and try again when policy is available.");
            var proposal = LocalServiceExtensionProposal.Prepare(_registry, service.Id, LocalExtensionHost.Text.Trim(),
                LocalExtensionPurpose.Text.Trim(), policy);
            ReviewFrozenProposal(RegistryVaultProposal.CreateEdit(proposal, policy));
        }
        catch (ArgumentException error) { ShowValidationError(error.Message); }
        catch (Exception) { ShowValidationError("The local exception could not be reviewed safely. No change was made."); }
    }

    private void RefreshRegistryHistory(VaultState state)
    {
        var infrastructure = state.GetInfrastructureCreatedHosts();
        InfrastructureHistory.Text = infrastructure.Count == 0 ? "No current permissions were created by infrastructure activation." :
            "Current infrastructure-created permissions (Blacklist still applies): " + string.Join(", ", infrastructure);
        var local = state.GetActiveLocalExtensions();
        LocalExtensionHistory.Text = local.Count == 0 ? "No current local exceptions." : string.Join("\n", local.Select(e =>
            $"{e.Proposal.ServiceName}: {e.Proposal.Hostname} — {e.Proposal.Purpose}"));
    }
}
