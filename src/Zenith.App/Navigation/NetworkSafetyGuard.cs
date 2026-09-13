using Microsoft.Web.WebView2.Core;
using Zenith.Core.Permissions;

namespace Zenith.App.Navigation;

/// <summary>Uses native request-source identity, never the selected page or its headers.</summary>
internal sealed class NetworkSafetyGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly Action _failed;
    private readonly NetworkSafetyPolicy _policy = new();

    internal NetworkSafetyGuard(CoreWebView2 core, Action failed)
    {
        _core = core;
        _failed = failed;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += Requested;
    }

    private void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var source = e.RequestedSourceKind switch
            {
                CoreWebView2WebResourceRequestSourceKinds.Document => NetworkRequestSource.Document,
                CoreWebView2WebResourceRequestSourceKinds.SharedWorker or CoreWebView2WebResourceRequestSourceKinds.ServiceWorker => NetworkRequestSource.BackgroundWorker,
                _ => NetworkRequestSource.Unknown
            };
            if (_policy.Allows(e.Request.Uri, source)) return;
        }
        catch (Exception) { /* Missing native metadata cannot authorize a request. */ }
        try
        {
            e.Response = _core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Zenith",
                "Content-Length: 0\r\nCache-Control: no-store");
        }
        catch (Exception) { _failed(); }
    }

    public void Dispose() => _core.WebResourceRequested -= Requested;
}
