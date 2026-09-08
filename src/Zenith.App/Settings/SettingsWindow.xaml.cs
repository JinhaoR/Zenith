using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Zenith.Core.Navigation;
using Zenith.Core.Access;
using Zenith.Core.Vault;

namespace Zenith.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly BrowserPreferencesStore _store;
    private readonly Action<BrowserPreferences> _applyPreferences;
    private readonly Action<Uri> _openSite;
    private readonly IReadOnlyList<StarterWhitelistSite> _sites;
    private readonly Func<IReadOnlyList<StarterWhitelistSite>>? _siteSource;
    private BrowserPreferences _preferences;
    private bool _loading = true;
    private readonly GreylistAccessService? _accessService;
    private readonly Action<Uri?>? _openAccess;
    private readonly DispatcherTimer _accessTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private PendingRequestItem[] _pendingItems = [];
    private ActiveVisitItem[] _activeVisitItems = [];
    private readonly Func<string>? _blacklistStatus;
    private readonly Func<string>? _adblockStatus;
    private readonly Func<Task>? _clearBrowsingData;
    private readonly Func<string>? _runtimeStatus;

    internal SettingsWindow(BrowserPreferencesStore store, BrowserPreferences preferences,
        Action<BrowserPreferences> applyPreferences, IReadOnlyList<StarterWhitelistSite> sites, Action<Uri> openSite,
        GreylistAccessService? accessService = null, Action<Uri?>? openAccess = null,
        VaultService? vaultService = null, Func<IReadOnlyList<StarterWhitelistSite>>? siteSource = null, Action? policyChanged = null,
        Func<string>? blacklistStatus = null, Func<string>? adblockStatus = null,
        Func<Task>? clearBrowsingData = null, Func<string>? runtimeStatus = null)
    {
        _store = store;
        _preferences = preferences;
        _applyPreferences = applyPreferences;
        _sites = sites;
        _siteSource = siteSource;
        _blacklistStatus = blacklistStatus;
        _adblockStatus = adblockStatus;
        _clearBrowsingData = clearBrowsingData;
        _runtimeStatus = runtimeStatus;
        _openSite = openSite;
        _accessService = accessService;
        _openAccess = openAccess;
        InitializeComponent();
        ClearBrowsingDataButton.IsEnabled = _clearBrowsingData is not null;
        RuntimeStatusText.Text = _runtimeStatus?.Invoke() ?? "Browser engine status is unavailable.";
        BlacklistStatusText.Text = _blacklistStatus?.Invoke() ?? "Blacklist status is unavailable in this window.";
        AdblockStatusText.Text = _adblockStatus?.Invoke() ?? "Resource-filter status is unavailable in this window.";
        VaultEditor.Configure(vaultService, () => { Close(); _openAccess?.Invoke(null); },
            () => { policyChanged?.Invoke(); RefreshSites(); RefreshAccess(); });
        ExpandedOption.IsChecked = preferences.StartSidebarExpanded;
        CompactOption.IsChecked = !preferences.StartSidebarExpanded;
        UpdateZoom();
        VersionText.Text = $"Development build · {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3)}";
        RefreshSites();
        _loading = false;
        SelectSection("General");
        RefreshAccess();
        _accessTimer.Tick += AccessTimer_OnTick;
        _accessTimer.Start();
        Closed += (_, _) =>
        {
            _accessTimer.Stop();
            _accessTimer.Tick -= AccessTimer_OnTick;
            VaultEditor.Detach();
        };
    }

    internal void SelectSection(string section)
    {
        Sections.SelectedItem = Sections.Items.Cast<ListBoxItem>().First(item => (string)item.Tag == section);
    }

    private void Sections_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || Sections.SelectedItem is not ListBoxItem { Tag: string section })
        {
            return;
        }

        foreach (var page in new[] { GeneralPage, SpherePage, AccessPage, VaultPage, AboutPage })
        {
            page.Visibility = Visibility.Collapsed;
        }
        var (panel, title, description) = section switch
        {
            "Sphere" => (SpherePage, "Your Sphere", "The places you can reach directly. Find a destination and open it."),
            "Access" => (AccessPage, "Temporary access", "Make room for an occasional, deliberate visit."),
            "Vault" => (VaultPage, "Vault", "The protected home for your long-term browsing choices."),
            "About" => (AboutPage, "About Zenith", "A quieter way to find your way around the Internet."),
            _ => (GeneralPage, "Make Zenith yours", "Small preferences for comfortable, everyday browsing.")
        };
        panel.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageDescription.Text = description;
        PageScrollViewer.ScrollToTop();
        RuntimeStatusText.Text = _runtimeStatus?.Invoke() ?? "Browser engine status is unavailable.";
        if (section == "Vault") VaultEditor.Refresh();
        if (section == "Sphere") RefreshSites();
    }

    private void Preferences_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            Save(_preferences with { StartSidebarExpanded = ExpandedOption.IsChecked == true });
        }
    }

    private async void ClearBrowsingData_OnClick(object sender, RoutedEventArgs e)
    {
        if (_clearBrowsingData is null || MessageBox.Show(this,
            "This closes all tabs and Zenith, signs you out of websites, and removes cookies, site storage, browser history, cache and saved autofill data. Unsaved website work will be lost. Vault rules, waits, password and Zenith bookmarks are kept.\n\nClear browsing data?",
            "Clear browsing data — Zenith", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        ClearBrowsingDataButton.IsEnabled = false;
        try { await _clearBrowsingData(); }
        catch (Exception)
        {
            MessageBox.Show("Browsing data could not be fully cleared. Zenith was stopped. Reopen it and retry; do not assume you have been signed out.",
                "Zenith", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ZoomOut_OnClick(object sender, RoutedEventArgs e) => ChangeZoom(-1);
    private void ZoomIn_OnClick(object sender, RoutedEventArgs e) => ChangeZoom(1);
    private void ResetZoom_OnClick(object sender, RoutedEventArgs e) => Save(_preferences with { DefaultZoomPercent = 100 });

    private void ChangeZoom(int direction)
    {
        var levels = BrowserPreferences.ZoomLevels.ToList();
        var index = Math.Clamp(levels.IndexOf(_preferences.DefaultZoomPercent) + direction, 0, levels.Count - 1);
        Save(_preferences with { DefaultZoomPercent = levels[index] });
    }

    private void Save(BrowserPreferences preferences)
    {
        try
        {
            _store.Save(preferences);
            _preferences = preferences;
            _applyPreferences(preferences);
            SaveStatus.Text = "Saved on this device.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SaveStatus.Text = "Preferences could not be saved. Check access to your local data folder and try again.";
            _loading = true;
            ExpandedOption.IsChecked = _preferences.StartSidebarExpanded;
            CompactOption.IsChecked = !_preferences.StartSidebarExpanded;
            _loading = false;
        }
        UpdateZoom();
    }

    private void UpdateZoom()
    {
        ZoomValue.Text = $"{_preferences.DefaultZoomPercent}%";
        ZoomOutButton.IsEnabled = _preferences.DefaultZoomPercent > BrowserPreferences.ZoomLevels[0];
        ZoomInButton.IsEnabled = _preferences.DefaultZoomPercent < BrowserPreferences.ZoomLevels[^1];
    }

    private void SiteFilter_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized)
        {
            RefreshSites();
        }
    }

    private void RefreshSites()
    {
        var query = SiteFilter.Text.Trim();
        var sites = (_siteSource?.Invoke() ?? _sites).Where(site => site.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            site.Host.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        SiteList.ItemsSource = sites;
        NoSitesText.Visibility = sites.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSite_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: StarterWhitelistSite site })
        {
            Close();
            _openSite(site.Target);
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void AccessTimer_OnTick(object? sender, EventArgs e)
    {
        if (AboutPage.Visibility == Visibility.Visible)
        {
            BlacklistStatusText.Text = _blacklistStatus?.Invoke() ?? "Blacklist status is unavailable in this window.";
            AdblockStatusText.Text = _adblockStatus?.Invoke() ?? "Resource-filter status is unavailable in this window.";
        }
        if (AccessPage.Visibility == Visibility.Visible) { RefreshAccess(); }
        if (VaultPage.Visibility == Visibility.Visible) { VaultEditor.Refresh(); }
    }

    private void RefreshAccess()
    {
        var configuration = _accessService?.ConfigurationState;
        var timing = _accessService?.Timing;
        var timingDescription = timing is null ? "Timing rules are unavailable." :
            $"Wait {DurationText.Format(timing.CooldownSeconds)}, then confirm with the same password for {DurationText.Format(timing.GrantSeconds)} of access. Exact hostname across tabs; access ends when Zenith closes.";
        SetupPasswordButton.Visibility = configuration == AccessConfigurationState.NeedsSetup ? Visibility.Visible : Visibility.Collapsed;
        AccessStatusText.Text = configuration switch
        {
            AccessConfigurationState.NeedsSetup => $"First, create your shared access and Vault password. {timingDescription}",
            AccessConfigurationState.Ready => $"Your password is configured. {timingDescription} Change these rules through the Vault.",
            AccessConfigurationState.Unavailable => "Protected data is unreadable or in use by another Zenith instance. If durable policy cannot be read, browsing is unavailable too. No password reset is offered here.",
            _ => "Temporary access is not available in this window."
        };
        var availability = _accessService?.GetAvailability();
        if (availability?.Phase == AccessPhase.ClockInvalid)
        {
            AccessStatusText.Text = "A clock change was detected. Correct the system clock and restart Zenith. Saved waits are retained, but temporary access is paused.";
        }
        else if (availability?.Phase == AccessPhase.Unavailable)
        {
            AccessStatusText.Text = "Protected access state is unavailable. Saved requests cannot be displayed safely. If durable policy is unreadable, browsing is also unavailable.";
        }
        var pending = _accessService?.GetPendingRequests()
            .Select(request => (Request: request, State: _accessService.GetStatus(request.Target)))
            .Where(item => item.State.Phase is AccessPhase.Cooldown or AccessPhase.SecondChallenge)
            .Select(item => new PendingRequestItem(new Uri(item.Request.Target),
                item.State.Phase == AccessPhase.SecondChallenge ? "Ready for your second confirmation" : $"Eligible at {item.Request.EligibleAt.ToLocalTime():g}"))
            .ToArray() ?? [];
        if (!_pendingItems.SequenceEqual(pending))
        {
            _pendingItems = pending;
            PendingRequests.ItemsSource = pending;
        }
        NoPendingRequests.Visibility = pending.Length == 0 && availability?.Phase is not (AccessPhase.ClockInvalid or AccessPhase.Unavailable)
            ? Visibility.Visible : Visibility.Collapsed;
        var visits = _accessService?.GetActiveGrants()
            .Select(grant => new ActiveVisitItem(grant.Site.Host, $"Available until {grant.ExpiresAt.ToLocalTime():T} · Ends when Zenith closes"))
            .ToArray() ?? [];
        if (!_activeVisitItems.SequenceEqual(visits))
        {
            _activeVisitItems = visits;
            ActiveVisits.ItemsSource = visits;
        }
        NoActiveVisits.Visibility = visits.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AccessAddress_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (AccessAddressFeedback is not null) AccessAddressFeedback.Text = string.Empty;
    }

    private void AccessAddress_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        BeginAccess_OnClick(sender, e);
    }

    private void BeginAccess_OnClick(object sender, RoutedEventArgs e)
    {
        if (!TypedAddressParser.TryParse(AccessAddress.Text, out var target))
        {
            AccessAddressFeedback.Text = "Enter a website address without embedded credentials, such as example.com. HTTPS is added automatically when omitted.";
            AccessAddress.Focus();
            return;
        }
        if (_accessService is null || _openAccess is null)
        {
            AccessAddressFeedback.Text = "Temporary access is unavailable in this window.";
            return;
        }

        // Core decides eligibility. This entry point does not navigate, authenticate
        // or create a request; the existing challenge window performs those steps.
        var status = _accessService.GetStatus(target.Target.AbsoluteUri);
        var message = status.Phase switch
        {
            AccessPhase.NotEligible => "This destination cannot use temporary access. Sites already in your Sphere can be opened there; Blacklisted sites cannot be visited.",
            AccessPhase.ClockInvalid => "Temporary access is paused after a clock change. Correct the system clock and restart Zenith; saved waits are retained.",
            AccessPhase.Unavailable => "Protected access state is unavailable. No visit has been requested.",
            AccessPhase.SetupRequired or AccessPhase.FirstChallenge or AccessPhase.Cooldown or
                AccessPhase.SecondChallenge or AccessPhase.Granted => null,
            _ => "This destination cannot be requested right now."
        };
        if (message is not null)
        {
            AccessAddressFeedback.Text = message;
            return;
        }

        Close();
        _openAccess(target.Target);
    }

    private void SetupPassword_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
        _openAccess?.Invoke(null);
    }

    private void ResumeAccess_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PendingRequestItem request })
        {
            Close();
            _openAccess?.Invoke(request.Target);
        }
    }

    private sealed record ActiveVisitItem(string Host, string Status);

    private sealed record PendingRequestItem(Uri Target, string Status)
    {
        public string Host => Target.IdnHost;
    }
    private void Window_OnSourceInitialized(object? sender, EventArgs e) => WindowFrameAppearance.Apply(this);

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
