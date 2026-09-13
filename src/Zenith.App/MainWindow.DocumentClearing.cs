using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Zenith.App.Navigation;

namespace Zenith.App;

public partial class MainWindow
{
    private void ClearTabWebContent(TabState tab)
    {
        if (tab.ControllerDestroyed) return;
        tab.Clearance ??= new DocumentClearance(
            callback => new ClearDeadline(Dispatcher, callback),
            () => DestroyClearingController(tab),
            destroyed => ConfirmDocumentRemoval(tab, destroyed));

        if (!tab.NavigationOperations.ScheduleInternalClear()) return;
        tab.Clearance.Begin();
        // Keep CurrentUri and IsStartSurface until removal is confirmed. Never show
        // or suspend the old document while waiting for its replacement to finish.
        HideAndSuspendTab(tab);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (_isClosing || !_tabs.Contains(tab) || tab.ControllerDestroyed) return;
            if (!tab.NavigationOperations.TryIssueInternalClear()) return;
            tab.Clearance.Issue(() =>
            {
                var core = tab.Browser.CoreWebView2
                    ?? throw new InvalidOperationException("The document controller is unavailable.");
                if (core.IsSuspended) core.Resume();
                core.Navigate("about:blank");
            });
        });
    }

    private void Browser_OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (sender is CoreWebView2 core && FindTab(core) is { } tab)
            tab.Clearance?.ProcessFailed();
    }

    private void DestroyClearingController(TabState tab)
    {
        tab.IsReady = false;
        tab.Browser.Visibility = Visibility.Collapsed;
        ++tab.LifecycleVersion;
        try
        {
            // Guard cleanup must not prevent controller disposal.
            try { DetachTabEvents(tab); }
            finally { tab.Browser.Dispose(); }
        }
        catch (Exception)
        {
            // Continuing could leave authenticated content alive with no dependable
            // controller. Do not report successful removal or retain the application.
            Environment.FailFast("Zenith could not destroy a document controller.");
        }
        tab.ControllerDestroyed = true;
        BrowserHost.Children.Remove(tab.Browser);
    }

    private void ConfirmDocumentRemoval(TabState tab, bool destroyed)
    {
        if (destroyed) tab.NavigationOperations.FailInternalClear();
        tab.IsStartSurface = true;
        tab.CurrentUri = null;
        tab.Favicon = null;
        tab.FaviconVersion++;
        tab.Title = "New tab";
        RefreshTabStrip();
        UpdateNavigationControls();
        if (destroyed && tab == _activeTab && !_isClosing)
            ShowNotice("The page could not be cleared. Its browser was closed. Open a new tab to continue.");
    }

    private sealed class ClearDeadline : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public ClearDeadline(Dispatcher dispatcher, Action expired)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Send, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _timer.Tick += (_, _) => { _timer.Stop(); expired(); };
            _timer.Start();
        }

        public void Dispose() => _timer.Stop();
    }
}
