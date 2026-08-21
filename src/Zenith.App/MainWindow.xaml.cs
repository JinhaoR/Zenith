using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App;

public partial class MainWindow : Window
{
    private readonly NavigationCoordinator _navigationCoordinator;
    private IInputElement? _noticeReturnFocus;
    private bool _initializationStarted;
    private bool _isClosing;
    private bool _webViewReady;

    internal MainWindow(NavigationCoordinator navigationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(navigationCoordinator);

        _navigationCoordinator = navigationCoordinator;
        InitializeComponent();
        UpdateBrowserHostBackground();
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

            Browser.CoreWebView2.NewWindowRequested += Browser_OnNewWindowRequested;
            Browser.CoreWebView2.HistoryChanged += Browser_OnHistoryChanged;
            _webViewReady = true;
            UpdateNavigationControls();
        }
        catch (Exception)
        {
            if (_isClosing)
            {
                return;
            }

            _webViewReady = false;
            ShowStartSurface();
            ShowNotice("Page rendering isn’t available right now, so pages can’t open.");
        }
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        _isClosing = true;

        Browser.NavigationStarting -= Browser_OnNavigationStarting;
        Browser.NavigationCompleted -= Browser_OnNavigationCompleted;

        if (Browser.CoreWebView2 is not null)
        {
            Browser.CoreWebView2.NewWindowRequested -= Browser_OnNewWindowRequested;
            Browser.CoreWebView2.HistoryChanged -= Browser_OnHistoryChanged;
        }

        Browser.Dispose();
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

        if (e.Key == Key.Escape)
        {
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
            Browser.GoBack();
            e.Handled = true;
            return;
        }

        if ((modifiers & ModifierKeys.Alt) != 0 && e.Key == Key.Right && ForwardButton.IsEnabled)
        {
            Browser.GoForward();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && ReloadButton.IsEnabled)
        {
            Browser.Reload();
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
    }

    private void AllSitesButton_OnClick(object sender, RoutedEventArgs e) =>
        ShowNotice("There are no sites in your Sphere yet.");

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) =>
        ShowNotice("Settings aren’t available yet.");

    private void DismissNoticeButton_OnClick(object sender, RoutedEventArgs e) =>
        HideNotice(restoreFocus: true);

    private void ReturnToSphereButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSphere();

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoBack)
        {
            Browser.GoBack();
        }
    }

    private void ForwardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Browser.CanGoForward)
        {
            Browser.GoForward();
        }
    }

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_webViewReady && Browser.Visibility == Visibility.Visible)
        {
            Browser.Reload();
        }
    }

    private void HomeButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSphere();

    private void Browser_OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        var decision = _navigationCoordinator.EvaluateWebViewRequest(e.Uri);

        if (decision is NavigationDecision.Allowed)
        {
            HideNotice();
            ShowBrowserSurface();
            return;
        }

        e.Cancel = true;
        ApplyNavigationDecision(decision, e.Uri);
    }

    private void Browser_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        var decision = _navigationCoordinator.EvaluateNewWindowRequest(e.Uri);
        ApplyNavigationDecision(decision, e.Uri);
    }

    private void Browser_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess && Browser.Visibility == Visibility.Visible)
        {
            var target = Browser.Source?.AbsoluteUri ?? "Unknown destination";
            ShowBoundarySurface(
                "This destination couldn’t open",
                target,
                "The page couldn’t finish loading. Try the address again or return to your Sphere.");
        }

        UpdateNavigationControls();
    }

    private void Browser_OnHistoryChanged(object? sender, object e) => UpdateNavigationControls();

    private void ApplyNavigationDecision(NavigationDecision decision, string requestedTarget)
    {
        switch (decision)
        {
            case NavigationDecision.Allowed allowed:
                NavigateTo(allowed.Target);
                break;
            case NavigationDecision.Denied denied:
                HideNotice();
                ShowBoundarySurface(
                    "This destination isn’t available",
                    requestedTarget,
                    denied.Reason switch
                    {
                        NavigationDenialReason.PolicyUnavailable =>
                            "Zenith can’t check whether this destination is in your Sphere right now. No page was opened.",
                        _ =>
                            "Zenith couldn’t confirm that this destination is in your Sphere. No page was opened."
                    });
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
        if (!_webViewReady)
        {
            HideNotice();
            ShowBoundarySurface(
                "This destination couldn’t open",
                target.AbsoluteUri,
                "Page rendering isn’t available right now. No page was opened.");
            return;
        }

        HideNotice();
        ShowBrowserSurface();
        Browser.Source = target;
    }

    private void FocusSphereSearch()
    {
        HideNotice();
        ShowStartSurface();
        SphereSearchTextBox.Focus();
        SphereSearchTextBox.SelectAll();
    }

    private void SearchSphere()
    {
        if (string.IsNullOrWhiteSpace(SphereSearchTextBox.Text))
        {
            SphereSearchTextBox.Focus();
            return;
        }

        ShowNotice("No places in your Sphere match that search yet.");
    }

    private void ReturnToSphere()
    {
        HideNotice();
        ShowStartSurface();
    }

    private void ShowStartSurface()
    {
        Browser.Visibility = Visibility.Collapsed;
        BoundarySurface.Visibility = Visibility.Collapsed;
        StartSurface.Visibility = Visibility.Visible;
        UpdateNavigationControls();
    }

    private void ShowBrowserSurface()
    {
        StartSurface.Visibility = Visibility.Collapsed;
        BoundarySurface.Visibility = Visibility.Collapsed;
        Browser.Visibility = Visibility.Visible;
        UpdateNavigationControls();
    }

    private void ShowBoundarySurface(string heading, string target, string explanation)
    {
        Browser.Visibility = Visibility.Collapsed;
        StartSurface.Visibility = Visibility.Collapsed;
        BoundarySurface.Visibility = Visibility.Visible;
        BoundaryHeadingTextBlock.Text = heading;
        BoundaryTargetTextBlock.Text = target;
        BoundaryTargetTextBlock.ToolTip = target;
        BoundaryExplanationTextBlock.Text = explanation;
        UpdateNavigationControls();

        var announcement = $"{heading}. {explanation} Requested address: {target}";
        AutomationProperties.SetName(BoundarySurface, announcement);
        AutomationProperties.SetHelpText(ReturnToSphereButton, announcement);
        var peer = UIElementAutomationPeer.FromElement(BoundarySurface)
            ?? UIElementAutomationPeer.CreatePeerForElement(BoundarySurface);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);

        ReturnToSphereButton.Focus();
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
        Browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(
            color.A,
            color.R,
            color.G,
            color.B);
    }

    private void UpdateNavigationControls()
    {
        var browserIsVisible = _webViewReady && Browser.Visibility == Visibility.Visible;
        BackButton.IsEnabled = browserIsVisible && Browser.CanGoBack;
        ForwardButton.IsEnabled = browserIsVisible && Browser.CanGoForward;
        ReloadButton.IsEnabled = browserIsVisible;
    }
}
