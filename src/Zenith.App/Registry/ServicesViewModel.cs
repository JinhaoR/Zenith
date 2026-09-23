using System.ComponentModel;
using Zenith.Core.Navigation;
using Zenith.Core.Registry;

namespace Zenith.App.Registry;

/// <summary>Local presentation and Core proposal requests; never navigates, persists or mutates policy.</summary>
internal sealed class ServicesViewModel : INotifyPropertyChanged
{
    private readonly Func<Task<RegistryLoadResult>> _load;
    private IReadOnlyList<ServicePresentation> _services = Array.Empty<ServicePresentation>();
    private string _searchText = string.Empty;
    private ServicePresentation? _selectedService;
    private bool _attempted;
    private ServiceRegistry? _registry;
    private Func<SitePolicySnapshot?>? _policySnapshot;
    private Action<AccessProposal>? _reviewInVault;
    public AccessProposal? Proposal { get; private set; }
    public IReadOnlyList<OptionalServiceScope> OptionalScopes { get; private set; } = Array.Empty<OptionalServiceScope>();
    public bool CanPrepareSelectedService => _policySnapshot is not null && _registry is not null && _selectedService is not null &&
        new ServiceAccessProposalBuilder().CanOffer(_registry, _selectedService.Id);
    public bool HasProposal => Proposal is not null;
    public bool CanContinue => Proposal?.CanStage == true && _reviewInVault is not null;
    public string ProposalSummary => Proposal is { } p ? ServiceProposalPresentation.Summary(p) : string.Empty;
    public string ProposalDetails => Proposal is { } p ? ServiceProposalPresentation.Details(p) : string.Empty;
    public string ProposalError { get; private set; } = string.Empty;

    internal void ConfigureProposals(Func<SitePolicySnapshot?> policySnapshot, Action<AccessProposal> reviewInVault)
    { _policySnapshot = policySnapshot; _reviewInVault = reviewInVault; Notify(); }

    internal void PrepareProposal(IReadOnlyList<string>? optionalHosts = null)
    {
        Proposal = null;
        ProposalError = string.Empty;
        try
        {
            if (!CanPrepareSelectedService || _policySnapshot!() is not { } policy)
                throw new InvalidOperationException("Reviewed service access or current policy is unavailable.");
            Proposal = new ServiceAccessProposalBuilder().Build(new(_selectedService!.Id, _registry!.Revision, optionalHosts), _registry, policy);
            OptionalScopes = Array.AsReadOnly(Proposal.OptionalDomains.Concat(Proposal.ProposedDomains.Where(d => !d.Required))
                .OrderBy(d => d.Hostname, StringComparer.Ordinal).Select(d => new OptionalServiceScope(d,
                    Proposal.SelectedService.OptionalHostnames.Contains(d.Hostname))).ToArray());
        }
        catch (Exception) { OptionalScopes = Array.Empty<OptionalServiceScope>(); ProposalError = "A safe service proposal could not be prepared. Reopen Services and review again when policy is available."; }
        Notify();
    }

    internal void ContinueToVault()
    {
        if (!CanContinue) return;
        try { _reviewInVault!(Proposal!); }
        catch (Exception) { ProposalError = "Vault could not review this proposal. Current permissions are unchanged."; Notify(); }
    }

    internal ServicesViewModel(Func<Task<RegistryLoadResult>> load) => _load = load;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<ServiceCategoryPresentation> Categories { get; private set; } = Array.Empty<ServiceCategoryPresentation>();
    public bool IsLoading { get; private set; }
    public bool IsAvailable { get; private set; }
    public string StatusText { get; private set; } = "Open Services to browse the local catalog.";
    public string Diagnostics { get; private set; } = string.Empty;
    public string RegistryRevision { get; private set; } = string.Empty;
    public bool HasDiagnostics => Diagnostics.Length > 0;
    public bool CanRetry => _attempted && !IsAvailable && !IsLoading;
    public string ResultText => IsAvailable ? $"{Categories.Sum(c => c.Services.Count)} services in the local catalog match." : string.Empty;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? string.Empty;
            Filter();
        }
    }

    public ServicePresentation? SelectedService
    {
        get => _selectedService;
        set
        {
            // Selection is a presentation object from this snapshot, never a Core ServiceSelection.
            if (value is not null && !Categories.Any(c => c.Services.Contains(value))) return;
            _selectedService = value;
            ClearProposal();
            Notify();
        }
    }

    internal Task EnsureLoadedAsync() => _attempted ? Task.CompletedTask : LoadAsync();

    internal async Task LoadAsync()
    {
        if (IsLoading) return;
        _attempted = true;
        IsLoading = true;
        IsAvailable = false;
        _registry = null;
        ClearProposal();
        _services = Array.Empty<ServicePresentation>();
        _selectedService = null;
        RegistryRevision = string.Empty;
        Diagnostics = string.Empty;
        StatusText = "Loading the local service catalog…";
        Filter();
        try
        {
            var result = await _load();
            if (result.Registry is not { } registry)
            {
                StatusText = "The service catalog is unavailable. Browsing permissions are unchanged.";
                Diagnostics = string.Join("\n", result.Issues.Select(i => $"{i.Location}: {i.Message}"));
            }
            else
            {
                var services = registry.ListServices().Select(s => new ServicePresentation(s, registry))
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                _services = Array.AsReadOnly(services);
                _registry = registry;
                RegistryRevision = registry.Revision;
                IsAvailable = true;
                StatusText = "Browse descriptions of services in Zenith’s local registry. This does not grant access or change your Sphere.";
            }
        }
        catch (Exception)
        {
            StatusText = "The service catalog is unavailable. Browsing permissions are unchanged.";
            Diagnostics = "The local catalog could not be loaded for presentation.";
        }
        finally
        {
            IsLoading = false;
            Filter();
        }
    }

    private void Filter()
    {
        var query = _searchText.Trim();
        var visible = _services.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            s.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase));
        Categories = Array.AsReadOnly(visible.GroupBy(s => s.CategoryId)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ServiceCategoryPresentation(g.Key, RegistryLabels.Category(g.Key), Array.AsReadOnly(g.ToArray()))).ToArray());
        if (_selectedService is not null && !Categories.Any(c => c.Services.Contains(_selectedService)))
        { _selectedService = null; ClearProposal(); }
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    private void ClearProposal() { Proposal = null; OptionalScopes = Array.Empty<OptionalServiceScope>(); ProposalError = string.Empty; }
}
