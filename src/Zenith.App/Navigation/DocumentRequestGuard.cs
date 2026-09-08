using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Zenith.Core.Navigation;

namespace Zenith.App.Navigation;

/// <summary>Pauses network documents before sending them, using CDP frame identity rather than URL heuristics.</summary>
internal sealed class DocumentRequestGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly NavigationCoordinator _navigation;
    private readonly Action _failed;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _requests;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _frames;
    private string? _mainFrame;
    private string? _topUrl;
    private bool _ready;
    private bool _disposed;
    private bool _faulted;

    internal DocumentRequestGuard(CoreWebView2 core, NavigationCoordinator navigation, Action failed)
    {
        _core = core;
        _navigation = navigation;
        _failed = failed;
        _requests = core.GetDevToolsProtocolEventReceiver("Fetch.requestPaused");
        _frames = core.GetDevToolsProtocolEventReceiver("Page.frameNavigated");
        _requests.DevToolsProtocolEventReceived += RequestPaused;
        _frames.DevToolsProtocolEventReceived += FrameNavigated;
        core.NavigationStarting += NavigationStarting;
    }

    internal async Task InitializeAsync()
    {
        await CallAsync("Page.enable", "{}");
        using var tree = JsonDocument.Parse(await CallAsync("Page.getFrameTree", "{}"));
        SetMainFrame(tree.RootElement.GetProperty("frameTree").GetProperty("frame"));
        if (string.IsNullOrEmpty(_mainFrame)) throw new InvalidOperationException("Missing main frame identity.");
        if (_disposed) return;
        await CallAsync("Fetch.enable", """
            {"patterns":[{"urlPattern":"*","resourceType":"Document","requestStage":"Request"}]}
            """);
        if (!_disposed) _ready = true;
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!_ready || _faulted) e.Cancel = true;
    }

    private void SetMainFrame(JsonElement frame)
    {
        _mainFrame = frame.GetProperty("id").GetString();
        _topUrl = frame.GetProperty("url").GetString();
    }

    private void FrameNavigated(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _faulted) return;
        try
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var frame = json.RootElement.GetProperty("frame");
            if (!frame.TryGetProperty("parentId", out _)) SetMainFrame(frame);
        }
        catch (Exception) { FailClosed(); }
    }

    private async void RequestPaused(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return; // The owning controller is being destroyed; never resume its requests.
        try
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var item = json.RootElement;
            var requestId = item.GetProperty("requestId").GetString();
            var frameId = item.GetProperty("frameId").GetString();
            var target = item.GetProperty("request").GetProperty("url").GetString();
            var allowed = !_faulted && !string.IsNullOrEmpty(_mainFrame) && !string.IsNullOrEmpty(frameId) &&
                !string.IsNullOrEmpty(target) && item.GetProperty("resourceType").GetString() == "Document" &&
                _navigation.EvaluateDocumentRequest(target, frameId == _mainFrame, _topUrl) is NavigationDecision.Allowed;
            if (string.IsNullOrEmpty(requestId)) { FailClosed(); return; }
            if (allowed)
                await CallAsync("Fetch.continueRequest", JsonSerializer.Serialize(new { requestId }));
            else
                await CallAsync("Fetch.failRequest", JsonSerializer.Serialize(new { requestId, errorReason = "BlockedByClient" }));
        }
        catch (Exception) { if (!_disposed) FailClosed(); }
    }

    private Task<string> CallAsync(string method, string parameters) =>
        _core.CallDevToolsProtocolMethodAsync(method, parameters).WaitAsync(TimeSpan.FromSeconds(10));

    private void FailClosed()
    {
        if (_faulted || _disposed) return;
        _faulted = true;
        _ready = false;
        try { _core.Stop(); }
        catch (Exception) { /* Controller disposal in the failure callback is the final boundary. */ }
        _failed();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _core.NavigationStarting -= NavigationStarting;
        _requests.DevToolsProtocolEventReceived -= RequestPaused;
        _frames.DevToolsProtocolEventReceived -= FrameNavigated;
        // Do not Fetch.disable: it could resume pending requests before controller disposal.
    }
}
