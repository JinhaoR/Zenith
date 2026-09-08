using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Zenith.App;

public partial class MainWindow
{
    private void BrowserVersionAvailable(object? sender, object e)
    {
        _runtimeUpdateAvailable = true;
        if (!_isClosing) ShowNotice("A browser engine update is available. Close all Zenith windows and reopen to use it.");
    }

    private string GetRuntimeStatus() => _browserEnvironment is null
        ? "Browser engine is not initialized."
        : $"WebView2 {_browserEnvironment.BrowserVersionString}\n" + (_runtimeUpdateAvailable
            ? "An engine update is available. Close Zenith and reopen to use it."
            : "Evergreen updates are managed by Microsoft. Restart Zenith regularly; this status is not a security certification.");

    private void PageIdentity_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeTab is not { IsStartSurface: false, IsReady: true } tab ||
            Zenith.Core.Navigation.WebsiteIdentity.FromAddress(tab.Browser.CoreWebView2.Source) is not { } identity)
        {
            ShowNotice("No website is currently open.");
            return;
        }
        MessageBox.Show(this, identity.Origin + "\n\n" + (identity.UsesHttps
            ? "This page uses HTTPS. Encryption does not establish that a website is trustworthy. Verify this address before signing in."
            : "This page uses unencrypted HTTP. Do not enter passwords or other private information.") +
            "\n\nUse Ctrl+L to inspect the complete address.", "Website identity — Zenith", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // Called only after native Settings confirmation. Destroy all live pages before
    // clearing the shared renderer profile, so tabs cannot immediately recreate cookies.
    internal async Task ClearBrowsingDataAndCloseAsync()
    {
        if (_clearingBrowsingData || _isClosing || _browserEnvironment is null)
            throw new InvalidOperationException("Browsing data is not available.");
        _clearingBrowsingData = true;
        IsEnabled = false;
        _accessTimer.Stop();
        using var maintenance = new WebView2();
        try
        {
            foreach (var tab in _tabs.ToArray())
            {
                tab.IsReady = false;
                DetachTabEvents(tab);
                tab.Browser.Dispose();
                BrowserHost.Children.Remove(tab.Browser);
            }
            _tabs.Clear();
            _activeTab = null;
            BrowserHost.Children.Add(maintenance);
            await maintenance.EnsureCoreWebView2Async(_browserEnvironment);
            var core = maintenance.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile).WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            BrowserHost.Children.Remove(maintenance);
            // Success or failure: do not resume browsing with a partly cleared profile.
            Close();
        }
    }
}
