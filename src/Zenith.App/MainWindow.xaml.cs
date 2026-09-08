using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Bookmarks;
using Zenith.App.Navigation;
using Zenith.App.Settings;
using Zenith.App.Access;
using Zenith.Core.Access;
using Zenith.Core.Vault;
using Zenith.Core.Navigation;

namespace Zenith.App;

public partial class MainWindow : Window
{
    private readonly NavigationCoordinator _navigationCoordinator;
    private readonly BrowserPreferencesStore _preferencesStore = new();
    private BrowserPreferences _preferences;
    private SettingsWindow? _settingsWindow;
    private readonly GreylistAccessService? _accessService;
    private readonly VaultService? _vaultService;
    private readonly Zenith.Core.Filtering.IBlacklistSource? _blacklist;
    private readonly Zenith.App.Filtering.AdblockService? _adblock;
    private long _displayedPolicyRevision = -1;
    private readonly DispatcherTimer _accessTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AccessWindow? _accessWindow;
    private Uri? _accessWindowTarget;
    private Uri? _temporaryAccessTarget;
    private readonly BookmarkStore _bookmarkStore = new();
    private readonly List<Bookmark> _bookmarks = [];
    private readonly List<TabState> _tabs = [];
    private readonly List<SphereResult> _visibleSphereResults = [];
    private readonly DispatcherTimer _bookmarkScrollBarHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(900)
    };
    private IInputElement? _noticeReturnFocus;
    private TabState? _activeTab;
    private bool _initializationStarted;
    private bool _isAddressEditing;
    private bool _isClosing;
    private bool _syncingBookmarkScrollBar;
    private bool _suppressSphereResultsRefresh;

    private sealed class TabState(WebView2 browser, string title)
    {
        public WebView2 Browser { get; } = browser;

        public string Title { get; set; } = title;

        public Uri? CurrentUri { get; set; }

        public bool IsStartSurface { get; set; } = true;

        public string? FaviconUri { get; set; }

        public bool IsReady { get; set; }

        public bool CoreEventsAttached { get; set; }
        public BrowserCapabilityGuard? CapabilityGuard { get; set; }
        public Zenith.App.Filtering.ResourceRequestGuard? ResourceGuard { get; set; }
        public DocumentRequestGuard? DocumentGuard { get; set; }
        public Zenith.App.Filtering.CosmeticFilterGuard? CosmeticGuard { get; set; }

        public NavigationOperationTracker NavigationOperations { get; } = new();

        public int LifecycleVersion { get; set; }
    }

    private sealed record SphereResult(
        string Title,
        Uri Target,
        SphereResultSource Source);

    private enum SphereResultSource
    {
        Site,
        Bookmark
    }

    private WebView2 ActiveBrowser => _activeTab?.Browser ?? Browser;

    internal MainWindow(NavigationCoordinator navigationCoordinator, GreylistAccessService? accessService = null, VaultService? vaultService = null,
        Zenith.Core.Filtering.IBlacklistSource? blacklist = null, Zenith.App.Filtering.AdblockService? adblock = null)
    {
        ArgumentNullException.ThrowIfNull(navigationCoordinator);

        _navigationCoordinator = navigationCoordinator;
        _accessService = accessService;
        _vaultService = vaultService;
        _blacklist = blacklist;
        _adblock = adblock;
        _preferences = _preferencesStore.Load();
        InitializeComponent();
        _bookmarkScrollBarHideTimer.Tick += BookmarkScrollBarHideTimer_OnTick;
        _bookmarks.AddRange(_bookmarkStore.Load());
        _activeTab = new TabState(Browser, "New tab");
        _tabs.Add(_activeTab);
        Browser.ZoomFactor = _preferences.DefaultZoomPercent / 100d;
        SetSidebarExpanded(_preferences.StartSidebarExpanded);
        UpdateBrowserHostBackground();
        if (_accessService is not null)
        {
            _accessTimer.Tick += AccessTimer_OnTick;
            _accessTimer.Start();
        }
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e) =>
        UpdateWindowFrameAppearance();

    private async Task InitializeTabAsync(TabState tab)
    {
        tab.Browser.NavigationStarting += Browser_OnNavigationStarting;
        tab.Browser.NavigationCompleted += Browser_OnNavigationCompleted;

        try
        {
            await tab.Browser.EnsureCoreWebView2Async(Browser.CoreWebView2?.Environment);

            if (_isClosing || !_tabs.Contains(tab))
            {
                return;
            }

            await AttachCapabilityGuardAsync(tab);
            if (_isClosing || !_tabs.Contains(tab)) return;
            tab.Browser.CoreWebView2.NewWindowRequested += Browser_OnNewWindowRequested;
            tab.Browser.CoreWebView2.HistoryChanged += Browser_OnHistoryChanged;
            tab.CoreEventsAttached = true;
            tab.IsReady = true;
            UpdateNavigationControls();
        }
        catch (Exception)
        {
            tab.IsReady = false;
            if (tab == _activeTab && !_isClosing)
            {
                ShowStartSurface();
                ShowNotice("Page rendering isn't available right now, so pages can't open.");
            }
        }
    }

    private void DetachTabEvents(TabState tab)
    {
        tab.DocumentGuard?.Dispose();
        tab.DocumentGuard = null;
        tab.CosmeticGuard?.Dispose();
        tab.CosmeticGuard = null;
        tab.ResourceGuard?.Dispose();
        tab.ResourceGuard = null;
        tab.CapabilityGuard?.Dispose();
        tab.CapabilityGuard = null;
        tab.Browser.NavigationStarting -= Browser_OnNavigationStarting;
        tab.Browser.NavigationCompleted -= Browser_OnNavigationCompleted;

        if (tab.CoreEventsAttached && tab.Browser.CoreWebView2 is not null)
        {
            tab.Browser.CoreWebView2.NewWindowRequested -= Browser_OnNewWindowRequested;
            tab.Browser.CoreWebView2.HistoryChanged -= Browser_OnHistoryChanged;
        }
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initializationStarted)
        {
            return;
        }

        _initializationStarted = true;
        Browser.NavigationStarting += Browser_OnNavigationStarting;
        Browser.NavigationCompleted += Browser_OnNavigationCompleted;

        try
        {
            await Browser.EnsureCoreWebView2Async();

            if (_isClosing)
            {
                return;
            }

            await AttachCapabilityGuardAsync(_tabs.First(tab => tab.Browser == Browser));
            if (_isClosing) return;
            Browser.CoreWebView2.NewWindowRequested += Browser_OnNewWindowRequested;
            Browser.CoreWebView2.HistoryChanged += Browser_OnHistoryChanged;
            if (_activeTab is not null)
            {
                _activeTab.IsReady = true;
            }
            UpdateNavigationControls();
        }
        catch (Exception)
        {
            if (_isClosing)
            {
                return;
            }

            ShowStartSurface();
            ShowNotice("Page rendering isn’t available right now, so pages can’t open.");
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _accessTimer.Stop();
        _accessTimer.Tick -= AccessTimer_OnTick;
        _bookmarkScrollBarHideTimer.Stop();
        _bookmarkScrollBarHideTimer.Tick -= BookmarkScrollBarHideTimer_OnTick;

        Browser.NavigationStarting -= Browser_OnNavigationStarting;
        Browser.NavigationCompleted -= Browser_OnNavigationCompleted;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.NewWindowRequested -= Browser_OnNewWindowRequested;
            Browser.CoreWebView2.HistoryChanged -= Browser_OnHistoryChanged;
        }

        _tabs.FirstOrDefault(tab => tab.Browser == Browser)?.CapabilityGuard?.Dispose();
        _tabs.FirstOrDefault(tab => tab.Browser == Browser)?.ResourceGuard?.Dispose();
        _tabs.FirstOrDefault(tab => tab.Browser == Browser)?.DocumentGuard?.Dispose();
        _tabs.FirstOrDefault(tab => tab.Browser == Browser)?.CosmeticGuard?.Dispose();
        Browser.Dispose();

        foreach (var tab in _tabs.Where(tab => tab.Browser != Browser))
        {
            DetachTabEvents(tab);
            tab.Browser.Dispose();
        }
    }

    private async Task AttachCapabilityGuardAsync(TabState tab)
    {
        tab.DocumentGuard = new DocumentRequestGuard(tab.Browser.CoreWebView2, _navigationCoordinator, () =>
        {
            tab.IsReady = false;
            // A failed interception channel cannot safely retain live controllers.
            // Close on the dispatcher after the native callback unwinds.
            if (!_isClosing) Dispatcher.BeginInvoke(Close);
        });
        await tab.DocumentGuard.InitializeAsync();
        if (_isClosing || !_tabs.Contains(tab)) return;
        if (_blacklist is not null)
            tab.ResourceGuard = new Zenith.App.Filtering.ResourceRequestGuard(tab.Browser.CoreWebView2, _blacklist, _adblock);
        tab.CapabilityGuard = new BrowserCapabilityGuard(tab.Browser.CoreWebView2,
            new Zenith.Core.Permissions.BrowserCapabilityPolicy(), message =>
            {
                if (!_isClosing && tab == _activeTab) ShowNotice(message);
            });
        if (_adblock is not null)
        {
            tab.CosmeticGuard = new Zenith.App.Filtering.CosmeticFilterGuard(tab.Browser.CoreWebView2, _adblock.Cosmetics);
            await tab.CosmeticGuard.InitializeAsync();
        }
    }

    private void BookmarkScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (BookmarkScrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        ShowBookmarkScrollBar();
    }

    private void BookmarkScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var hasOverflow = BookmarkScrollViewer.ScrollableHeight > 0;
        BookmarkOverlayScrollBar.Visibility = hasOverflow
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!hasOverflow)
        {
            _bookmarkScrollBarHideTimer.Stop();
            BookmarkOverlayScrollBar.Opacity = 0;
            BookmarkOverlayScrollBar.IsHitTestVisible = false;
            return;
        }

        _syncingBookmarkScrollBar = true;
        BookmarkOverlayScrollBar.Maximum = BookmarkScrollViewer.ScrollableHeight;
        BookmarkOverlayScrollBar.ViewportSize = BookmarkScrollViewer.ViewportHeight;
        BookmarkOverlayScrollBar.LargeChange = BookmarkScrollViewer.ViewportHeight;
        BookmarkOverlayScrollBar.Value = BookmarkScrollViewer.VerticalOffset;
        _syncingBookmarkScrollBar = false;
    }

    private void BookmarkOverlayScrollBar_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingBookmarkScrollBar)
        {
            return;
        }

        BookmarkScrollViewer.ScrollToVerticalOffset(e.NewValue);
        ShowBookmarkScrollBar();
    }

    private void ShowBookmarkScrollBar()
    {
        BookmarkOverlayScrollBar.Opacity = 0.55;
        BookmarkOverlayScrollBar.IsHitTestVisible = true;
        _bookmarkScrollBarHideTimer.Stop();
        _bookmarkScrollBarHideTimer.Start();
    }

    private void BookmarkScrollBarHideTimer_OnTick(object? sender, EventArgs e)
    {
        if (BookmarkOverlayScrollBar.IsMouseOver ||
            BookmarkOverlayScrollBar.IsMouseCaptureWithin)
        {
            return;
        }

        BookmarkOverlayScrollBar.Opacity = 0;
        BookmarkOverlayScrollBar.IsHitTestVisible = false;
        _bookmarkScrollBarHideTimer.Stop();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e) =>
        SphereResultsPopup.IsOpen = false;

    private void MainWindow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SphereResultsPopup.IsOpen ||
            SphereSearchContainer.IsMouseOver ||
            SphereResultsPopup.Child is UIElement { IsMouseOver: true })
        {
            return;
        }

        SphereResultsPopup.IsOpen = false;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.K)
        {
            FocusSphereSearch();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.L)
        {
            FocusAddressEditing();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.T)
        {
            _ = CreateTabAsync();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.W && _activeTab is not null)
        {
            CloseTab(_activeTab);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_isAddressEditing)
            {
                CancelAddressEditing();
                e.Handled = true;
                return;
            }

            if (SphereResultsPopup.IsOpen)
            {
                SphereResultsPopup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (BoundarySurface.Visibility == Visibility.Visible)
            {
                ReturnToSphere();
                e.Handled = true;
                return;
            }

            if (NoticeBar.Visibility == Visibility.Visible)
            {
                HideNotice(restoreFocus: true);
                e.Handled = true;
                return;
            }
        }

        if ((modifiers & ModifierKeys.Alt) != 0 && e.Key == Key.Left && BackButton.IsEnabled)
        {
            ActiveBrowser.GoBack();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Alt) != 0 && e.Key == Key.Right && ForwardButton.IsEnabled)
        {
            ActiveBrowser.GoForward();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && ReloadButton.IsEnabled)
        {
            ActiveBrowser.Reload();
            e.Handled = true;
        }

        if ((modifiers & ModifierKeys.Control) != 0 && e.Key == Key.D && BookmarkButton.IsEnabled)
        {
            ToggleBookmark();
            e.Handled = true;
        }
    }

    private void SphereSearchTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        SearchSphere();
        e.Handled = true;
    }

    private void SphereSearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SphereSearchPlaceholder.Visibility = string.IsNullOrEmpty(SphereSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_suppressSphereResultsRefresh &&
            !_isAddressEditing &&
            SphereSearchTextBox.IsKeyboardFocusWithin)
        {
            RefreshSphereResults();
            SphereResultsPopup.IsOpen = true;
        }
    }

    private void SphereSearchTextBox_OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_isAddressEditing)
        {
            SphereResultsPopup.IsOpen = false;
            return;
        }

        RefreshSphereResults();
        SphereResultsPopup.IsOpen = true;
    }

    private void SphereSearchTextBox_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (_isAddressEditing)
        {
            CancelAddressEditing(restoreBrowserFocus: false);
        }
    }

    private void OpenSettingsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSettings("General");
    }

    private void SettingsMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenSettings("General");

    private void VaultMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenSettings("Vault");

    private void AboutMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        OpenSettings("About");

    private void OpenSettings(string section)
    {
        SphereResultsPopup.IsOpen = false;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_preferencesStore, _preferences, ApplyPreferences,
                GetSphereSites(),
                target => RequestNavigation(target.AbsoluteUri, NavigationOrigin.AddressBar),
                _accessService, OpenTemporaryAccess, _vaultService, GetSphereSites, RefreshPolicyViews,
                () => (_blacklist as Zenith.App.Filtering.BlacklistUpdater)?.Status ?? "No synchronized source is available in this test window.",
                () => _adblock?.Status ?? "No resource-filter engine is available in this test window.") { Owner = this };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.SelectSection(section);
        _settingsWindow.Activate();
    }

    private void ApplyPreferences(BrowserPreferences preferences)
    {
        if (_preferences.DefaultZoomPercent != preferences.DefaultZoomPercent)
        {
            foreach (var tab in _tabs)
            {
                tab.Browser.ZoomFactor = preferences.DefaultZoomPercent / 100d;
            }
        }
        _preferences = preferences;
    }

    private void RequestAccess_OnClick(object sender, RoutedEventArgs e)
    {
        if (_temporaryAccessTarget is not null) { OpenTemporaryAccess(_temporaryAccessTarget); }
    }

    private void OpenTemporaryAccess(Uri? target)
    {
        if (_accessService is null) { return; }
        if (_accessWindow is not null && _accessWindowTarget != target) { _accessWindow.Close(); }
        if (_accessWindow is null)
        {
            _accessWindowTarget = target;
            _accessWindow = new AccessWindow(_accessService, target,
                destination => RequestNavigation(destination.AbsoluteUri, NavigationOrigin.AddressBar)) { Owner = this };
            _accessWindow.Closed += (_, _) => _accessWindow = null;
            _accessWindow.Show();
        }
        _accessWindow.Activate();
    }

    private void AccessTimer_OnTick(object? sender, EventArgs e) => ValidateRetainedTabs();

    internal void ValidateRetainedTabs()
    {
        if (_isClosing || _accessService is null) { return; }
        var revision = _vaultService?.GetAccessTimingSafely()?.Revision ?? -1;
        if (revision != _displayedPolicyRevision)
        {
            _displayedPolicyRevision = revision;
            RefreshPolicyViews();
        }
        _ = _accessService.GetPendingRequests();
        var changed = false;
        foreach (var tab in _tabs.ToArray())
        {
            if (tab.IsStartSurface || tab.CurrentUri is not { } target) { continue; }
            var decision = _navigationCoordinator.EvaluateWebViewRequest(target.AbsoluteUri);
            if (decision is NavigationDecision.Allowed) { continue; }
            changed = true;
            if (tab == _activeTab)
            {
                ApplyNavigationDecision(decision, target.AbsoluteUri);
            }
            else
            {
                ClearTabWebContent(tab);
                HideAndSuspendTab(tab);
            }
        }
        if (changed) { RefreshTabStrip(); }
    }

    private void RefreshPolicyViews()
    {
        RefreshBookmarkGrid();
        RefreshSphereResults();
        UpdateBookmarkButton();
    }

    internal void ApplyBlacklistUpdate()
    {
        if (_isClosing) return;
        // Unload retained frame trees as well as top-level pages. Newly Blacklisted
        // content may already be embedded in any tab; a URL-only scan misses it.
        var hadPages = _tabs.Any(tab => !tab.IsStartSurface);
        foreach (var tab in _tabs.Where(tab => !tab.IsStartSurface).ToArray())
        {
            HideAndSuspendTab(tab);
            ClearTabWebContent(tab);
        }
        if (hadPages) ShowStartSurface();
        RefreshTabStrip();
        RefreshPolicyViews();
        if (hadPages) ShowNotice("Blacklist updated. Open your destinations again to load them with the latest protection.");
    }

    private IReadOnlyList<StarterWhitelistSite> GetSphereSites()
    {
        if (_vaultService is null) return DevelopmentStarterPolicy.Sites.Where(site => IsTargetInSphere(site.Target)).ToArray();
        if (!_vaultService.TryGetActivePolicy(out var policy)) return [];
        return policy.Entries.Where(entry => entry.AccessClass == AccessClass.Whitelist && policy.Classify(entry.Identity) == AccessClass.Whitelist)
            .Select(entry => new StarterWhitelistSite(
                DevelopmentStarterPolicy.Sites.FirstOrDefault(site => site.Host == entry.Identity.Host)?.Name ?? entry.Identity.Host,
                entry.Identity.Host, new UriBuilder("https", entry.Identity.Host).Uri.AbsoluteUri, entry.IncludeSubdomains)).ToArray();
    }

    private void BookmarksMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        FocusSphereSearch();
    }

    private void CollapseSidebarButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSidebarExpanded(SidebarColumn.Width.Value < 100);
    }

    private void CollapsedSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetSidebarExpanded(true);
        FocusSphereSearch();
    }

    private void AddBookmarkButton_OnClick(object sender, RoutedEventArgs e)
    {
        FocusSphereSearch();
    }

    private void CurrentSiteButton_OnClick(object sender, RoutedEventArgs e) =>
        FocusAddressEditing();

    private void BookmarkShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Bookmark bookmark })
        {
            RequestNavigation(bookmark.Target.AbsoluteUri, NavigationOrigin.AddressBar);
        }
    }

    private void NewTabButton_OnClick(object sender, RoutedEventArgs e) => _ = CreateTabAsync();

    private void TabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TabState tab })
        {
            ActivateTab(tab);
        }
    }

    private void CloseTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: TabState tab })
        {
            return;
        }

        CloseTab(tab);
    }

    private void BookmarkButton_OnClick(object sender, RoutedEventArgs e) => ToggleBookmark();

    private void SphereResultButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SphereResult result })
        {
            OpenSphereResult(result);
        }
    }

    private void SphereResultBookmarkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SphereResult result })
        {
            return;
        }

        ToggleBookmark(result.Target, result.Title);
        RefreshSphereResults();
        SphereResultsPopup.IsOpen = true;
        e.Handled = true;
    }

    private void CloseTab(TabState tab)
    {
        if (_tabs.Count == 1)
        {
            ReturnToSphere();
            return;
        }

        var tabIndex = _tabs.IndexOf(tab);
        var wasActive = tab == _activeTab;
        DetachTabEvents(tab);
        BrowserHost.Children.Remove(tab.Browser);
        tab.Browser.Dispose();
        _tabs.Remove(tab);

        if (wasActive)
        {
            ActivateTab(_tabs[Math.Min(tabIndex, _tabs.Count - 1)]);
        }
        else
        {
            RefreshTabStrip();
        }
    }

    private void DismissNoticeButton_OnClick(object sender, RoutedEventArgs e) =>
        HideNotice(restoreFocus: true);

    private void ReturnToSphereButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSphere();

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ActiveBrowser.CanGoBack)
        {
            ActiveBrowser.GoBack();
        }
    }

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ActiveBrowser.CanGoForward)
        {
            ActiveBrowser.GoForward();
        }
    }

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeTab?.IsReady == true && ActiveBrowser.Visibility == Visibility.Visible)
        {
            ActiveBrowser.Reload();
        }
    }

    private void HomeButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSphere();

    private void Browser_OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (sender is not WebView2 browser || FindTab(browser) is not { } tab)
        {
            e.Cancel = true;
            return;
        }

        if (tab.NavigationOperations.TryRecordInternalClearStarting(e.Uri, e.NavigationId))
        {
            return;
        }

        if (tab.NavigationOperations.IsInternalClearPending)
        {
            // A native surface is replacing this page. Cancel any late page-driven
            // navigation while the host-issued clear is being serialized.
            e.Cancel = true;
            return;
        }

        if (NavigationOperationTracker.IsBlankTarget(e.Uri))
        {
            // Only a tracked host clear may navigate to the internal blank page.
            // Cancel unmatched blank requests without replacing the current surface.
            e.Cancel = true;
            return;
        }

        var decision = _navigationCoordinator.EvaluateWebViewRequest(e.Uri);

        if (decision is NavigationDecision.Allowed allowed)
        {
            tab.NavigationOperations.RecordExternal(e.NavigationId, allowed.Target);
            tab.CurrentUri = allowed.Target;
            tab.IsStartSurface = false;
            tab.Title = tab.CurrentUri.Host;
            RefreshTabStrip();
            HideNotice();
            if (tab == _activeTab)
            {
                ShowBrowserSurface();
                UpdateCurrentSiteIdentity();
            }
            return;
        }

        e.Cancel = true;
        if (tab == _activeTab)
        {
            ApplyNavigationDecision(decision, e.Uri);
        }
    }

    private void Browser_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not CoreWebView2 coreWebView || FindTab(coreWebView) is not { } tab)
        {
            return;
        }

        var decision = _navigationCoordinator.EvaluateNewWindowRequest(e.Uri);
        if (decision is NavigationDecision.Allowed allowed)
        {
            _ = CreateTabAsync(allowed.Target);
        }
        else if (tab == _activeTab)
        {
            ApplyNavigationDecision(decision, e.Uri);
        }
    }

    private void Browser_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not WebView2 browser || FindTab(browser) is not { } tab)
        {
            return;
        }

        var completion = tab.NavigationOperations.MatchCompletion(e.NavigationId);
        if (completion.Kind == NavigationCompletionKind.Stale)
        {
            return;
        }

        if (completion.Kind == NavigationCompletionKind.InternalClear)
        {
            if (completion.DeferredTarget is { } deferredTarget)
            {
                StartNavigation(tab, deferredTarget);
                return;
            }

            if (tab.IsStartSurface || browser.Visibility != Visibility.Visible)
            {
                HideAndSuspendTab(tab);
            }
            return;
        }

        if (e.IsSuccess)
        {
            tab.CurrentUri = browser.Source;
            tab.Title = browser.CoreWebView2?.DocumentTitle ?? string.Empty;
            tab.FaviconUri = browser.CoreWebView2?.FaviconUri;
            if (string.IsNullOrWhiteSpace(tab.Title))
            {
                tab.Title = tab.CurrentUri?.Host ?? "New tab";
            }

            RefreshTabStrip();
            UpdateBookmarkButton();
            if (tab == _activeTab)
            {
                UpdateCurrentSiteIdentity();
            }
        }

        if (!e.IsSuccess && tab == _activeTab && browser.Visibility == Visibility.Visible)
        {
            var target = completion.RequestedTarget?.AbsoluteUri ?? "Unknown destination";
            ShowBoundarySurface(
                "This destination couldn’t open",
                target,
                "The page couldn’t finish loading. Try the address again or return to your Sphere.");
        }

        if (tab != _activeTab || browser.Visibility != Visibility.Visible)
        {
            HideAndSuspendTab(tab);
        }

        UpdateNavigationControls();
    }

    private void Browser_OnHistoryChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2 coreWebView ||
            FindTab(coreWebView) is not { IsStartSurface: false } tab ||
            string.IsNullOrWhiteSpace(coreWebView.Source))
        {
            return;
        }

        var source = coreWebView.Source;
        if (NavigationOperationTracker.IsBlankTarget(source))
        {
            // History notifications can still describe the initial/cleared document
            // after StartNavigation has selected an external destination. This is an
            // observation, not a request to navigate to about:blank.
            UpdateNavigationControls();
            return;
        }

        var decision = _navigationCoordinator.EvaluateWebViewRequest(source);
        if (decision is NavigationDecision.Allowed allowed)
        {
            tab.CurrentUri = allowed.Target;
            if (tab == _activeTab)
            {
                UpdateCurrentSiteIdentity();
                UpdateNavigationControls();
            }
        }
        else if (tab == _activeTab)
        {
            ApplyNavigationDecision(decision, source);
        }
        else
        {
            ClearTabWebContent(tab);
            HideAndSuspendTab(tab);
        }
    }

    private void ApplyNavigationDecision(NavigationDecision decision, string requestedTarget)
    {
        switch (decision)
        {
            case NavigationDecision.Allowed allowed:
                NavigateTo(allowed.Target);
                break;
            case NavigationDecision.Denied denied:
                HideNotice();
                var (heading, explanation) = denied.Reason switch
                {
                    NavigationDenialReason.PolicyUnavailable =>
                        ("Zenith can’t check this destination",
                         "The active Site Policy is unavailable, so no page was opened."),
                    NavigationDenialReason.UnsupportedTarget =>
                        ("This address can’t be opened",
                         "Zenith can open only complete HTTP or HTTPS addresses without embedded credentials. No page was opened."),
                    NavigationDenialReason.Greylisted =>
                        ("This destination isn’t in your Sphere",
                         "This destination is outside your Sphere. You can request a temporary visit after a waiting period and two password challenges."),
                    NavigationDenialReason.Blacklisted =>
                        ("This destination is unavailable",
                         "This destination is Blacklisted and cannot be opened while that policy remains active."),
                    _ =>
                        ("This destination isn’t available",
                         "Zenith couldn’t confirm that this destination is in your Sphere. No page was opened.")
                };
                ShowBoundarySurface(
                    heading,
                    requestedTarget,
                    explanation);
                if (denied.Reason == NavigationDenialReason.Greylisted && _accessService is not null &&
                    NavigationUriNormalizer.TryNormalize(requestedTarget, out var accessTarget))
                {
                    _temporaryAccessTarget = accessTarget.Target;
                    RequestAccessButton.Visibility = Visibility.Visible;
                }
                break;
            default:
                HideNotice();
                ShowBoundarySurface(
                    "This destination isn’t available",
                    requestedTarget,
                    "Zenith couldn’t confirm that this destination is in your Sphere. No page was opened.");
                break;
        }
    }

    private void NavigateTo(Uri target)
    {
        if (_activeTab is not { } tab || !tab.IsReady)
        {
            HideNotice();
            ShowBoundarySurface(
                "This destination couldn’t open",
                target.AbsoluteUri,
                "Page rendering isn’t available right now. No page was opened.");
            return;
        }

        QueueNavigation(tab, target);
    }

    private void QueueNavigation(TabState tab, Uri target)
    {
        var disposition = tab.NavigationOperations.PrepareExternal(target);
        if (disposition == ExternalNavigationDisposition.DeferUntilInternalClearCompletes)
        {
            return;
        }

        StartNavigation(tab, target);
    }

    private void StartNavigation(TabState tab, Uri target)
    {
        tab.IsStartSurface = false;
        tab.CurrentUri = target;
        tab.Title = target.Host;
        if (tab == _activeTab)
        {
            HideNotice();
            ShowBrowserSurface();
        }
        RefreshTabStrip();
        tab.Browser.Source = target;
    }

    private void RequestNavigation(string target, NavigationOrigin origin)
    {
        var decision = origin switch
        {
            NavigationOrigin.AddressBar => _navigationCoordinator.EvaluateAddressBarRequest(target),
            NavigationOrigin.NewWindow => _navigationCoordinator.EvaluateNewWindowRequest(target),
            _ => _navigationCoordinator.EvaluateWebViewRequest(target)
        };

        ApplyNavigationDecision(decision, target);
    }

    private async Task CreateTabAsync(Uri? target = null)
    {
        if (_isClosing)
        {
            return;
        }

        var browser = new WebView2
        {
            Visibility = Visibility.Collapsed,
            ZoomFactor = _preferences.DefaultZoomPercent / 100d,
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 8, 19, 33)
        };
        BrowserHost.Children.Add(browser);
        var tab = new TabState(browser, "New tab");
        _tabs.Add(tab);
        UpdateBrowserHostBackground();
        ActivateTab(tab);
        await InitializeTabAsync(tab);

        if (target is not null && tab.IsReady && !_isClosing && _tabs.Contains(tab))
        {
            // Initialization yields to the dispatcher. The user may have switched
            // tabs while waiting; navigate the new controller, never the active one.
            var decision = _navigationCoordinator.EvaluateNewWindowRequest(target.AbsoluteUri);
            if (decision is NavigationDecision.Allowed allowed)
            {
                QueueNavigation(tab, allowed.Target);
            }
            else if (tab == _activeTab)
            {
                ApplyNavigationDecision(decision, target.AbsoluteUri);
            }
        }
    }

    private void ActivateTab(TabState tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        foreach (var item in _tabs)
        {
            if (item != tab)
            {
                HideAndSuspendTab(item);
            }
        }

        _activeTab = tab;
        if (tab.IsStartSurface || !tab.IsReady)
        {
            ShowStartSurface();
        }
        else
        {
            // A retained document must be reauthorized before it becomes visible,
            // including when its grant expired between host timer ticks.
            var decision = _navigationCoordinator.EvaluateWebViewRequest(tab.CurrentUri?.AbsoluteUri ?? string.Empty);
            if (decision is NavigationDecision.Allowed) { ShowBrowserSurface(); }
            else { ApplyNavigationDecision(decision, tab.CurrentUri?.AbsoluteUri ?? string.Empty); }
        }

        RefreshTabStrip();
        UpdateBookmarkButton();
    }

    private TabState? FindTab(WebView2 browser) =>
        _tabs.FirstOrDefault(tab => tab.Browser == browser);

    private TabState? FindTab(CoreWebView2 coreWebView) =>
        _tabs.FirstOrDefault(tab => tab.Browser.CoreWebView2 == coreWebView);

    private void FocusSphereSearch()
    {
        _isAddressEditing = false;
        HideNotice();
        SetSidebarExpanded(true);
        SetSphereSearchText(string.Empty);
        SphereSearchTextBox.Focus();
        RefreshSphereResults();
        SphereResultsPopup.IsOpen = true;
    }

    private void FocusAddressEditing()
    {
        if (_activeTab?.CurrentUri is not { } currentUri || _activeTab.IsStartSurface)
        {
            FocusSphereSearch();
            return;
        }

        HideNotice();
        SetSidebarExpanded(true);
        _isAddressEditing = true;
        SetSphereSearchText(currentUri.AbsoluteUri);
        SphereResultsPopup.IsOpen = false;
        SphereSearchTextBox.Focus();
        SphereSearchTextBox.SelectAll();
    }

    private void CancelAddressEditing(bool restoreBrowserFocus = true)
    {
        _isAddressEditing = false;
        SetSphereSearchText(string.Empty);
        SphereResultsPopup.IsOpen = false;

        if (restoreBrowserFocus && ActiveBrowser.Visibility == Visibility.Visible)
        {
            ActiveBrowser.Focus();
        }
    }

    private void SetSphereSearchText(string value)
    {
        _suppressSphereResultsRefresh = true;
        SphereSearchTextBox.Text = value;
        _suppressSphereResultsRefresh = false;
    }

    private void SearchSphere()
    {
        var query = SphereSearchTextBox.Text.Trim();
        _isAddressEditing = false;
        if (string.IsNullOrWhiteSpace(query))
        {
            RefreshSphereResults();
            SphereResultsPopup.IsOpen = true;
            SphereSearchTextBox.Focus();
            return;
        }

        if (TypedAddressParser.TryParse(query, out var directTarget))
        {
            SphereResultsPopup.IsOpen = false;
            SetSphereSearchText(string.Empty);
            RequestNavigation(directTarget.Target.AbsoluteUri, NavigationOrigin.AddressBar);
            return;
        }

        RefreshSphereResults();
        if (_visibleSphereResults.FirstOrDefault() is { } result)
        {
            OpenSphereResult(result);
            return;
        }

        ShowNotice("No places in your Sphere match that search yet.");
    }

    private void ReturnToSphere()
    {
        if (_activeTab is not null)
        {
            _activeTab.IsStartSurface = true;
            _activeTab.Title = "New tab";
            ClearTabWebContent(_activeTab);
        }
        HideNotice();
        ShowStartSurface();
        RefreshTabStrip();
    }

    private void ShowStartSurface()
    {
        if (_activeTab is { } tab)
        {
            HideAndSuspendTab(tab);
        }
        BoundarySurface.Visibility = Visibility.Collapsed;
        StartSurface.Visibility = Visibility.Visible;
        UpdateCurrentSiteIdentity();
        UpdateNavigationControls();
    }

    private void ShowBrowserSurface()
    {
        SphereResultsPopup.IsOpen = false;
        StartSurface.Visibility = Visibility.Collapsed;
        BoundarySurface.Visibility = Visibility.Collapsed;
        if (_activeTab is { } tab)
        {
            ResumeAndShowTab(tab);
        }
        UpdateCurrentSiteIdentity();
        UpdateNavigationControls();
    }

    private void ShowBoundarySurface(string heading, string target, string explanation)
    {
        _temporaryAccessTarget = null;
        RequestAccessButton.Visibility = Visibility.Collapsed;
        SphereResultsPopup.IsOpen = false;
        if (_activeTab is { } tab)
        {
            ClearTabWebContent(tab);
            HideAndSuspendTab(tab);
        }
        StartSurface.Visibility = Visibility.Collapsed;
        BoundarySurface.Visibility = Visibility.Visible;
        BoundaryHeadingTextBlock.Text = heading;
        BoundaryTargetTextBlock.Text = target;
        BoundaryTargetTextBlock.ToolTip = target;
        BoundaryExplanationTextBlock.Text = explanation;
        UpdateCurrentSiteIdentity();
        UpdateNavigationControls();

        var announcement = $"{heading}. {explanation} Requested address: {target}";
        AutomationProperties.SetName(BoundarySurface, announcement);
        AutomationProperties.SetHelpText(ReturnToSphereButton, announcement);
        var peer = UIElementAutomationPeer.FromElement(BoundarySurface)
            ?? UIElementAutomationPeer.CreatePeerForElement(BoundarySurface);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        ReturnToSphereButton.Focus();
    }

    private void ClearTabWebContent(TabState tab)
    {
        tab.IsStartSurface = true;
        tab.CurrentUri = null;
        tab.FaviconUri = null;
        tab.Title = "New tab";

        if (!tab.IsReady ||
            tab.Browser.CoreWebView2 is null)
        {
            tab.NavigationOperations.AbandonExternal();
            return;
        }

        if (!tab.NavigationOperations.ScheduleInternalClear())
        {
            return;
        }

        var browser = tab.Browser;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () =>
            {
                if (_isClosing ||
                    !_tabs.Contains(tab) ||
                    browser.CoreWebView2 is null ||
                    !tab.NavigationOperations.TryIssueInternalClear())
                {
                    return;
                }

                try
                {
                    browser.CoreWebView2.Navigate("about:blank");
                }
                catch (Exception)
                {
                    var deferredTarget = tab.NavigationOperations.FailInternalClear();
                    if (deferredTarget is not null)
                    {
                        StartNavigation(tab, deferredTarget);
                        return;
                    }

                    HideAndSuspendTab(tab);
                }
            });
    }

    private void HideAndSuspendTab(TabState tab)
    {
        tab.Browser.Visibility = Visibility.Collapsed;
        var lifecycleVersion = ++tab.LifecycleVersion;

        if (!tab.IsReady ||
            tab.NavigationOperations.IsInternalClearPending ||
            tab.Browser.CoreWebView2 is not { } coreWebView)
        {
            return;
        }

        _ = TrySuspendTabAsync(tab, coreWebView, lifecycleVersion);
    }

    private async Task TrySuspendTabAsync(
        TabState tab,
        CoreWebView2 coreWebView,
        int lifecycleVersion)
    {
        try
        {
            if (!coreWebView.IsSuspended)
            {
                await coreWebView.TrySuspendAsync();
            }

            if (!_isClosing &&
                (tab.LifecycleVersion != lifecycleVersion ||
                 tab.Browser.Visibility == Visibility.Visible) &&
                coreWebView.IsSuspended)
            {
                coreWebView.Resume();
            }
        }
        catch (Exception)
        {
            // Suspension is best-effort. Native surfaces still unload replaced content.
        }
    }

    private void ResumeAndShowTab(TabState tab)
    {
        ++tab.LifecycleVersion;
        if (tab.IsReady && tab.Browser.CoreWebView2 is { IsSuspended: true } coreWebView)
        {
            try
            {
                coreWebView.Resume();
            }
            catch (Exception)
            {
                // Making the controller visible also requests an automatic resume.
            }
        }

        tab.Browser.Visibility = Visibility.Visible;
    }

    private void UpdateCurrentSiteIdentity()
    {
        if (_activeTab is not { IsStartSurface: false, CurrentUri: { } target } tab ||
            tab.Browser.Visibility != Visibility.Visible ||
            BoundarySurface.Visibility == Visibility.Visible)
        {
            CurrentSiteButton.Visibility = Visibility.Collapsed;
            return;
        }

        var host = SiteIdentity.TryCreate(target.Host, out var identity)
            ? identity.Host
            : target.IdnHost;
        CurrentSiteText.Text = host;
        CurrentSiteButton.ToolTip = target.AbsoluteUri;
        CurrentSiteButton.Visibility = Visibility.Visible;
        AutomationProperties.SetName(
            CurrentSiteButton,
            $"Current site {host}. Show and edit the full address.");
        AutomationProperties.SetHelpText(CurrentSiteButton, target.AbsoluteUri);
    }

    private void ShowNotice(string message)
    {
        if (!NoticeBar.IsKeyboardFocusWithin)
        {
            _noticeReturnFocus = Keyboard.FocusedElement;
        }

        NoticeTextBlock.Text = message;
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void HideNotice(bool restoreFocus = false)
    {
        if (NoticeBar.Visibility != Visibility.Visible)
        {
            return;
        }

        NoticeBar.Visibility = Visibility.Collapsed;
        var returnFocus = _noticeReturnFocus;
        _noticeReturnFocus = null;

        if (!restoreFocus)
        {
            return;
        }

        if (returnFocus is null || Keyboard.Focus(returnFocus) is null)
        {
            HomeButton.Focus();
        }
    }

    internal void UpdateBrowserHostBackground()
    {
        if (TryFindResource("ZenithWindowBrush") is not System.Windows.Media.SolidColorBrush brush)
        {
            return;
        }

        var color = brush.Color;
        var background = System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        foreach (var tab in _tabs)
        {
            tab.Browser.DefaultBackgroundColor = background;
        }
    }

    internal void UpdateWindowFrameAppearance() => WindowFrameAppearance.Apply(this);

    private void UpdateNavigationControls()
    {
        var browserIsVisible = _activeTab?.IsReady == true && ActiveBrowser.Visibility == Visibility.Visible;
        BackButton.IsEnabled = browserIsVisible && ActiveBrowser.CanGoBack;
        ForwardButton.IsEnabled = browserIsVisible && ActiveBrowser.CanGoForward;
        ReloadButton.IsEnabled = browserIsVisible;
        BookmarkButton.IsEnabled = browserIsVisible && _activeTab?.CurrentUri is not null;
        UpdateBookmarkButton();
    }

    private void RefreshSphereResults()
    {
        var query = SphereSearchTextBox.Text.Trim();
        var bookmarkResults = _bookmarks
            .Where(bookmark => IsTargetInSphere(bookmark.Target))
            .Where(bookmark => MatchesSphereQuery(bookmark.Title, bookmark.Host, query))
            .OrderBy(bookmark => bookmark.Title, StringComparer.OrdinalIgnoreCase)
            .Select(bookmark => new SphereResult(
                bookmark.Title,
                bookmark.Target,
                SphereResultSource.Bookmark))
            .ToList();
        var siteResults = GetSphereSites()
            .Where(site => IsTargetInSphere(site.Target))
            .Where(site => MatchesSphereQuery(site.Name, site.Host, query))
            .OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
            .Select(site => new SphereResult(
                site.Name,
                site.Target,
                SphereResultSource.Site))
            .ToList();

        _visibleSphereResults.Clear();
        _visibleSphereResults.AddRange(bookmarkResults);
        _visibleSphereResults.AddRange(siteResults);
        SphereResultsPanel.Children.Clear();
        SphereResultsHeadingTextBlock.Text = query.Length == 0
            ? $"{siteResults.Count} sites · {bookmarkResults.Count} bookmarks"
            : $"Results for “{query}”";

        if (bookmarkResults.Count > 0)
        {
            AddSphereResultSection("BOOKMARKS", bookmarkResults);
        }

        if (siteResults.Count > 0)
        {
            AddSphereResultSection(query.Length == 0 ? "ALL SITES" : "SITES", siteResults);
        }

        if (_visibleSphereResults.Count == 0)
        {
            SphereResultsPanel.Children.Add(new TextBlock
            {
                Margin = new Thickness(8, 14, 8, 18),
                Text = "No places in your Sphere match this search.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("ZenithMutedTextBrush")
            });
        }
    }

    private void AddSphereResultSection(string heading, IEnumerable<SphereResult> results)
    {
        SphereResultsPanel.Children.Add(new TextBlock
        {
            Margin = new Thickness(8, 7, 8, 5),
            Text = heading,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ZenithSubtleTextBrush")
        });

        foreach (var result in results)
        {
            SphereResultsPanel.Children.Add(CreateSphereResultRow(result));
        }
    }

    private Grid CreateSphereResultRow(SphereResult result)
    {
        var row = new Grid { Height = 52, Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var openButton = new Button
        {
            Tag = result,
            Style = (Style)FindResource("SidebarTextButtonStyle"),
            Height = 52,
            Padding = new Thickness(7, 0, 3, 0),
            ToolTip = result.Target.AbsoluteUri,
            Content = CreateSphereResultContent(result)
        };
        AutomationProperties.SetName(openButton, $"Open {result.Title}");
        openButton.Click += SphereResultButton_OnClick;
        row.Children.Add(openButton);

        var isBookmarked = IsBookmarked(result.Target);
        var bookmarkButton = new Button
        {
            Tag = result,
            Style = (Style)FindResource("SidebarIconButtonStyle"),
            Width = 32,
            Height = 32,
            Margin = new Thickness(2, 10, 2, 10),
            Content = isBookmarked ? "\uE735" : "\uE734",
            Foreground = (Brush)FindResource(
                isBookmarked ? "ZenithAccentBrush" : "ZenithMutedTextBrush"),
            ToolTip = isBookmarked ? "Remove bookmark" : "Add bookmark"
        };
        AutomationProperties.SetName(
            bookmarkButton,
            isBookmarked
                ? $"Remove {result.Title} from bookmarks"
                : $"Add {result.Title} to bookmarks");
        bookmarkButton.Click += SphereResultBookmarkButton_OnClick;
        Grid.SetColumn(bookmarkButton, 1);
        row.Children.Add(bookmarkButton);

        return row;
    }

    private Grid CreateSphereResultContent(SphereResult result)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(CreateSphereSiteIcon(result.Target.Host));

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = result.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("ZenithTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Text = result.Source == SphereResultSource.Bookmark
                ? $"{result.Target.Host} · Bookmark"
                : $"{result.Target.Host} · Site",
            FontSize = 10,
            Foreground = (Brush)FindResource("ZenithSubtleTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(labels, 1);
        content.Children.Add(labels);
        return content;
    }

    private Border CreateSphereSiteIcon(string host)
    {
        return new Border
        {
            Width = 30,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)FindResource("ZenithSurfaceBrush"),
            CornerRadius = new CornerRadius(8),
            Child = CreateSiteIconContent(host, 17)
        };
    }

    private UIElement CreateSiteIconContent(string host, double size)
    {
        var geometry = FindStarterSiteIcon(host);
        return geometry is null
            ? new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = size,
                Foreground = (Brush)FindResource("ZenithAccentBrush"),
                Text = "\uE774"
            }
            : new Viewbox
            {
                Width = size,
                Height = size,
                Child = new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Fill = (Brush)FindResource("ZenithAccentBrush"),
                    Stretch = Stretch.Uniform
                }
            };
    }

    private void RefreshBookmarkGrid()
    {
        var expanded = SidebarColumn.Width.Value >= 100;
        BookmarkGrid.Children.Clear();
        BookmarkGrid.Columns = expanded ? 3 : 1;

        foreach (var bookmark in _bookmarks.Where(bookmark => IsTargetInSphere(bookmark.Target)))
        {
            BookmarkGrid.Children.Add(CreateBookmarkButton(bookmark, expanded));
        }

        AddBookmarkButton.Width = expanded ? 54 : 32;
        AddBookmarkButton.Height = expanded ? 54 : 32;
        AddBookmarkButton.Margin = expanded
            ? new Thickness(0, 0, 8, 8)
            : new Thickness(0, 0, 0, 4);
        BookmarkGrid.Children.Add(AddBookmarkButton);
    }

    private Button CreateBookmarkButton(Bookmark bookmark, bool expanded)
    {
        var size = expanded ? 54 : 32;
        var openButton = new Button
        {
            Tag = bookmark,
            Style = (Style)FindResource("BookmarkIconButtonStyle"),
            Width = size,
            Height = size,
            Margin = expanded
                ? new Thickness(0, 0, 8, 8)
                : new Thickness(0, 0, 0, 4),
            ToolTip = bookmark.Title,
            Content = CreateSiteIconContent(bookmark.Host, expanded ? 22 : 17)
        };
        AutomationProperties.SetName(openButton, $"Open bookmark {bookmark.Title}");
        openButton.Click += BookmarkShortcutButton_OnClick;
        return openButton;
    }

    private static bool MatchesSphereQuery(string title, string host, string query)
    {
        return query.Length == 0 ||
               title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               host.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsTargetInSphere(Uri target)
    {
        return _navigationCoordinator.EvaluateAddressBarRequest(target.AbsoluteUri)
            is NavigationDecision.Allowed { AccessClass: AccessClass.Whitelist };
    }

    private void OpenSphereResult(SphereResult result)
    {
        SphereResultsPopup.IsOpen = false;
        _suppressSphereResultsRefresh = true;
        SphereSearchTextBox.Clear();
        _suppressSphereResultsRefresh = false;
        RequestNavigation(result.Target.AbsoluteUri, NavigationOrigin.AddressBar);
    }

    private void ToggleBookmark()
    {
        if (_activeTab?.CurrentUri is not { } target || _activeTab.IsStartSurface)
        {
            return;
        }

        ToggleBookmark(target, _activeTab.Title);
    }

    private void ToggleBookmark(Uri target, string title)
    {
        var index = _bookmarks.FindIndex(bookmark =>
            TargetsEqual(bookmark.Target, target));
        if (index >= 0)
        {
            _bookmarks.RemoveAt(index);
            _bookmarkStore.Save(_bookmarks);
            ShowNotice("Removed from bookmarks.");
        }
        else
        {
            if (!IsTargetInSphere(target))
            {
                ShowNotice("Only places in your Sphere can be bookmarked.");
                return;
            }

            _bookmarks.Add(new Bookmark(title, target));
            _bookmarkStore.Save(_bookmarks);
            ShowNotice("Saved to bookmarks.");
        }

        UpdateBookmarkButton();
        RefreshBookmarkGrid();
    }

    private bool IsBookmarked(Uri target) =>
        _bookmarks.Any(bookmark => TargetsEqual(bookmark.Target, target));

    private static bool TargetsEqual(Uri left, Uri right) =>
        Uri.Compare(
            left,
            right,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private void UpdateBookmarkButton()
    {
        if (_activeTab?.CurrentUri is not { } target || _activeTab.IsStartSurface)
        {
            BookmarkButton.IsEnabled = false;
            BookmarkButton.Content = "\uE734";
            BookmarkButton.Foreground = (Brush)FindResource("ZenithMutedTextBrush");
            BookmarkButton.ToolTip = "Bookmark this page (Ctrl+D)";
            AutomationProperties.SetName(BookmarkButton, "Bookmark this page");
            return;
        }

        var isBookmarked = IsBookmarked(target);
        BookmarkButton.IsEnabled = _activeTab.IsReady && (isBookmarked || IsTargetInSphere(target));
        BookmarkButton.Content = isBookmarked ? "\uE735" : "\uE734";
        BookmarkButton.Foreground = (Brush)FindResource(
            isBookmarked ? "ZenithAccentBrush" : "ZenithMutedTextBrush");
        BookmarkButton.ToolTip = isBookmarked
            ? "Remove bookmark (Ctrl+D)"
            : "Bookmark this page (Ctrl+D)";
        AutomationProperties.SetName(
            BookmarkButton,
            isBookmarked ? "Remove bookmark" : "Bookmark this page");
    }

    private void RefreshTabStrip()
    {
        OpenTabsPanel.Children.Clear();
        var expanded = SidebarColumn.Width.Value >= 100;

        foreach (var tab in _tabs)
        {
            var row = new Grid
            {
                Height = 40,
                Margin = new Thickness(0, 0, 0, 4),
                Background = Brushes.Transparent
            };

            var tabButton = new Button
            {
                Tag = tab,
                Style = (Style)FindResource("SidebarTextButtonStyle"),
                Padding = expanded ? new Thickness(10, 0, 36, 0) : new Thickness(0),
                HorizontalContentAlignment = expanded
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Center,
                Background = tab == _activeTab
                    ? (Brush)FindResource("ZenithRaisedSurfaceBrush")
                    : Brushes.Transparent,
                ToolTip = tab.Title,
                Content = CreateTabButtonContent(tab, expanded)
            };
            AutomationProperties.SetName(tabButton, $"Switch to {tab.Title}");
            tabButton.Click += TabButton_OnClick;
            row.Children.Add(tabButton);

            var closeButton = new Button
            {
                Tag = tab,
                Style = (Style)FindResource("SidebarIconButtonStyle"),
                Width = expanded ? 24 : 17,
                Height = expanded ? 24 : 17,
                Margin = expanded
                    ? new Thickness(0, 0, 5, 0)
                    : new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = expanded
                    ? VerticalAlignment.Center
                    : VerticalAlignment.Top,
                Background = expanded
                    ? Brushes.Transparent
                    : (Brush)FindResource("ZenithRaisedSurfaceBrush"),
                BorderBrush = expanded
                    ? Brushes.Transparent
                    : (Brush)FindResource("ZenithInteractiveBorderBrush"),
                BorderThickness = expanded ? new Thickness(2) : new Thickness(1),
                Content = "\uE711",
                FontSize = expanded ? 10 : 7,
                ToolTip = "Close tab",
                Visibility = Visibility.Collapsed
            };
            AutomationProperties.SetName(closeButton, $"Close {tab.Title}");
            closeButton.Click += CloseTabButton_OnClick;
            Panel.SetZIndex(closeButton, 1);
            row.Children.Add(closeButton);

            void UpdateCloseButtonVisibility()
            {
                closeButton.Visibility = row.IsMouseOver || row.IsKeyboardFocusWithin
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            row.MouseEnter += (_, _) => UpdateCloseButtonVisibility();
            row.MouseLeave += (_, _) => UpdateCloseButtonVisibility();
            row.IsKeyboardFocusWithinChanged += (_, _) => UpdateCloseButtonVisibility();

            OpenTabsPanel.Children.Add(row);
        }
    }

    private StackPanel CreateTabButtonContent(TabState tab, bool expanded)
    {
        var brandGeometry = FindStarterSiteIcon(tab.CurrentUri?.Host);
        var favicon = brandGeometry is null
            ? CreateFaviconSource(tab.FaviconUri)
            : null;
        UIElement iconContent;
        if (brandGeometry is not null)
        {
            iconContent = new Viewbox
            {
                Width = 15,
                Height = 15,
                Child = new System.Windows.Shapes.Path
                {
                    Data = brandGeometry,
                    Fill = (Brush)FindResource("ZenithAccentBrush"),
                    Stretch = Stretch.Uniform
                }
            };
        }
        else if (favicon is not null)
        {
            iconContent = new Image
            {
                Source = favicon,
                Width = 15,
                Height = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            iconContent = new TextBlock
            {
                Text = tab.IsStartSurface ? "Z" : tab.Title[..1].ToUpperInvariant(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("ZenithAccentBrush")
            };
        }

        var icon = new Border
        {
            Width = 22,
            Height = 22,
            Margin = expanded ? new Thickness(0, 0, 9, 0) : new Thickness(0),
            Background = (Brush)FindResource("ZenithSurfaceBrush"),
            CornerRadius = new CornerRadius(7),
            Child = iconContent
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                icon,
                new TextBlock
                {
                    Text = tab.Title,
                    Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
    }

    private Geometry? FindStarterSiteIcon(string? host)
    {
        var resourceKey = host switch
        {
            not null when HostMatches(host, "wikipedia.org") => "WikipediaIconGeometry",
            not null when HostMatches(host, "github.com") => "GitHubIconGeometry",
            not null when HostMatches(host, "youtube.com") => "YouTubeIconGeometry",
            not null when HostMatches(host, "chatgpt.com") || HostMatches(host, "openai.com") => "OpenAiIconGeometry",
            not null when HostMatches(host, "google.com") => "GoogleIconGeometry",
            not null when HostMatches(host, "stackoverflow.com") => "StackOverflowIconGeometry",
            not null when HostMatches(host, "gitlab.com") => "GitLabIconGeometry",
            not null when HostMatches(host, "developer.mozilla.org") => "MdnIconGeometry",
            not null when HostMatches(host, "archive.org") => "InternetArchiveIconGeometry",
            not null when HostMatches(host, "reddit.com") => "RedditIconGeometry",
            not null when HostMatches(host, "learn.microsoft.com") => "MicrosoftIconGeometry",
            _ => null
        };

        return resourceKey is null ? null : TryFindResource(resourceKey) as Geometry;
    }

    private static bool HostMatches(string host, string expectedHost)
    {
        return host.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith($".{expectedHost}", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource? CreateFaviconSource(string? faviconUri)
    {
        if (!Uri.TryCreate(faviconUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        try
        {
            return new BitmapImage(uri);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void SetSidebarExpanded(bool expanded)
    {
        if (!expanded)
        {
            SphereResultsPopup.IsOpen = false;
        }

        SidebarColumn.Width = new GridLength(expanded ? 248 : 64);
        SphereSearchContainer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CollapsedSearchButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        BookmarksSectionLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        TabsSectionLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        NewTabText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SettingsButtonText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CurrentSiteText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        CurrentSiteIcon.Margin = expanded ? new Thickness(0, 0, 9, 0) : new Thickness(0);
        CurrentSiteButton.Padding = expanded ? new Thickness(10, 0, 10, 0) : new Thickness(0);
        CurrentSiteButton.HorizontalContentAlignment = expanded
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        TabsRow.MinHeight = expanded ? 0 : 110;
        TabsRow.Margin = expanded
            ? new Thickness(4, 0, 4, 8)
            : new Thickness(0, 0, 0, 8);
        NewTabButton.Padding = expanded ? new Thickness(10, 0, 10, 0) : new Thickness(0);
        NewTabButton.HorizontalContentAlignment = expanded
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        NewTabIcon.Margin = expanded ? new Thickness(0, 0, 9, 0) : new Thickness(0);
        SettingsButton.Padding = expanded ? new Thickness(10, 0, 10, 0) : new Thickness(0);
        SettingsButton.HorizontalContentAlignment = expanded
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        SettingsButtonIcon.Margin = expanded ? new Thickness(0, 0, 9, 0) : new Thickness(0);
        RefreshBookmarkGrid();
        NavigationButtonsPanel.HorizontalAlignment = expanded
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Center;
        NavigationButtonsPanel.Orientation = expanded
            ? Orientation.Horizontal
            : Orientation.Vertical;
        NavigationButtonsPanel.Margin = expanded
            ? new Thickness(0, 0, 0, 12)
            : new Thickness(0, 0, 0, 8);
        CollapseSidebarButton.HorizontalAlignment = expanded
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        HomeButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(CollapseSidebarButton, expanded ? 1 : 0);
        SetCollapsedButtonSizes(expanded);
        CollapseSidebarButton.Content = expanded ? "\uE76B" : "\uE76C";
        CollapseSidebarButton.ToolTip = expanded ? "Collapse sidebar" : "Expand sidebar";
        AutomationProperties.SetName(
            CollapseSidebarButton,
            expanded ? "Collapse sidebar" : "Expand sidebar");
        RefreshTabStrip();
    }

    private void SetCollapsedButtonSizes(bool expanded)
    {
        var size = expanded ? 38 : 32;
        foreach (var button in NavigationButtonsPanel.Children.OfType<Button>())
        {
            button.Width = size;
            button.Height = size;
            button.Margin = expanded
                ? new Thickness(0, 0, 4, 0)
                : new Thickness(0, 0, 0, 4);
        }

        CollapsedSearchButton.Width = expanded ? 38 : 32;
        CollapsedSearchButton.Height = expanded ? 38 : 32;
        CollapsedSearchButton.Margin = new Thickness(0);
    }
}
