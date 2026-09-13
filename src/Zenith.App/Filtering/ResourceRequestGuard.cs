using Microsoft.Web.WebView2.Core;
using Zenith.Core.Filtering;

namespace Zenith.App.Filtering;

internal sealed class ResourceRequestGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly ResourceFilteringPolicy _policy;
    private readonly Action _failed;
    private string _mainTarget;
    internal ResourceRequestGuard(CoreWebView2 core, IBlacklistSource source, IResourceFilterEngine? advertisements, Action failed)
    {
        _core = core;
        _failed = failed;
        _mainTarget = core.Source;
        _policy = new ResourceFilteringPolicy(source, advertisements);
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.Document);
        core.WebResourceRequested += Requested;
        core.NavigationStarting += NavigationStarting;
    }
    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.Cancel) _mainTarget = e.Uri;
    }
    private void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            EvaluateRequest(e);
        }
        catch (Exception)
        {
            // Any metadata/evaluation failure must produce a local denial. If the
            // native response channel itself is unavailable, destroy the controllers.
            try { Deny(e); }
            catch (Exception) { _failed(); }
        }
    }

    private void Deny(CoreWebView2WebResourceRequestedEventArgs e) =>
        e.Response = _core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Zenith",
            "Content-Length: 0\r\nCache-Control: no-store");

    private void EvaluateRequest(CoreWebView2WebResourceRequestedEventArgs e)
    {
        // The safety guard denies worker/unknown sources without borrowing page context.
        if (e.RequestedSourceKind != CoreWebView2WebResourceRequestSourceKinds.Document) return;
        var headers = e.Request.Headers;
        var source = headers.Contains("Referer") ? headers.GetHeader("Referer") : _core.Source;
        var destination = headers.Contains("Sec-Fetch-Dest") ? headers.GetHeader("Sec-Fetch-Dest") : "";
        var kind = e.ResourceContext switch
        {
            CoreWebView2WebResourceContext.Document => destination is "iframe" or "frame" ? ResourceKind.Frame : ResourceDocumentContext.Kind(e.Request.Uri, _mainTarget),
            CoreWebView2WebResourceContext.Script => ResourceKind.Script,
            CoreWebView2WebResourceContext.Stylesheet => ResourceKind.Stylesheet,
            CoreWebView2WebResourceContext.Image => ResourceKind.Image,
            CoreWebView2WebResourceContext.Font => ResourceKind.Font,
            CoreWebView2WebResourceContext.Media => ResourceKind.Media,
            CoreWebView2WebResourceContext.XmlHttpRequest or CoreWebView2WebResourceContext.Fetch => ResourceKind.Fetch,
            CoreWebView2WebResourceContext.Ping => ResourceKind.Ping,
            CoreWebView2WebResourceContext.Websocket => ResourceKind.WebSocket,
            _ => ResourceKind.Other
        };
        var decision = _policy.Evaluate(new(e.Request.Uri, source, kind));
        if (decision != ResourceFilterDecision.Allow) Deny(e);
    }
    public void Dispose()
    {
        _core.WebResourceRequested -= Requested;
        _core.NavigationStarting -= NavigationStarting;
    }
}
