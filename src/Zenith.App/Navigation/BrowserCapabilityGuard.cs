using Microsoft.Web.WebView2.Core;
using Zenith.Core.Permissions;

namespace Zenith.App.Navigation;

internal sealed class BrowserCapabilityGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly BrowserCapabilityPolicy _policy;
    private readonly Action<string> _notify;

    internal BrowserCapabilityGuard(CoreWebView2 core, BrowserCapabilityPolicy policy, Action<string> notify)
    {
        _core = core;
        _policy = policy;
        _notify = notify;
        core.PermissionRequested += PermissionRequested;
        core.DownloadStarting += DownloadStarting;
        core.LaunchingExternalUriScheme += LaunchingExternalUriScheme;
        core.ServerCertificateErrorDetected += ServerCertificateErrorDetected;
        core.ClientCertificateRequested += ClientCertificateRequested;
        core.BasicAuthenticationRequested += BasicAuthenticationRequested;
    }

    internal async Task InitializeAsync(bool enableCosmeticMessages)
    {
        var settings = _core.Settings;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = enableCosmeticMessages;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsReputationCheckingRequired = true;
        await _core.ClearServerCertificateErrorActionsAsync();
        // Old renderer grants must not bypass PermissionRequested in an existing profile.
        foreach (var permission in await _core.Profile.GetNonDefaultPermissionSettingsAsync())
            await _core.Profile.SetPermissionStateAsync(permission.PermissionKind, permission.PermissionOrigin,
                CoreWebView2PermissionState.Default);
    }

    private void ServerCertificateErrorDetected(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        _notify(_policy.Evaluate(BrowserCapability.InvalidServerCertificate).Explanation);
    }

    private void ClientCertificateRequested(object? sender, CoreWebView2ClientCertificateRequestedEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
        _notify(_policy.Evaluate(BrowserCapability.ClientCertificate).Explanation);
    }

    private void BasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
    {
        e.Cancel = true;
        _notify(_policy.Evaluate(BrowserCapability.HttpAuthentication).Explanation);
    }

    private void PermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Set fail-closed values before presentation, and don't create renderer-owned
        // durable permission decisions outside the Vault.
        e.State = CoreWebView2PermissionState.Deny;
        e.Handled = true;
        e.SavesInProfile = false;
        var decision = _policy.Evaluate(BrowserCapability.PermissionRequest);
        if (decision.Allowed) e.State = CoreWebView2PermissionState.Allow;
        else _notify(decision.Explanation);
    }

    private void DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
        var decision = _policy.Evaluate(BrowserCapability.Download);
        if (decision.Allowed) e.Cancel = false;
        else _notify(decision.Explanation);
    }

    private void LaunchingExternalUriScheme(object? sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e)
    {
        e.Cancel = true;
        var decision = _policy.Evaluate(BrowserCapability.ExternalApplication);
        if (decision.Allowed) e.Cancel = false;
        else _notify(decision.Explanation);
    }

    public void Dispose()
    {
        _core.PermissionRequested -= PermissionRequested;
        _core.DownloadStarting -= DownloadStarting;
        _core.LaunchingExternalUriScheme -= LaunchingExternalUriScheme;
        _core.ServerCertificateErrorDetected -= ServerCertificateErrorDetected;
        _core.ClientCertificateRequested -= ClientCertificateRequested;
        _core.BasicAuthenticationRequested -= BasicAuthenticationRequested;
    }
}
