using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Zenith.Core.Navigation;

namespace Zenith.App.Navigation;

/// <summary>
/// Adapts native frame navigation to Core policy, with root-target CDP document
/// interception as additional request-stage protection.
/// </summary>
internal sealed class DocumentRequestGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly NavigationCoordinator _navigation;
    private readonly Action _failed;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _requests;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _frames;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _networkFailures;
    private readonly HashSet<string> _cancelledDocuments = [];
    private readonly Queue<string> _cancelledOrder = [];
    private readonly Dictionary<CoreWebView2Frame, EventHandler<object>> _nativeFrames = [];
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
        _networkFailures = core.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
        _requests.DevToolsProtocolEventReceived += RequestPaused;
        _frames.DevToolsProtocolEventReceived += FrameNavigated;
        _networkFailures.DevToolsProtocolEventReceived += NetworkLoadingFailed;
        core.NavigationStarting += NavigationStarting;
        core.FrameNavigationStarting += NativeFrameNavigationStarting;
        core.FrameCreated += NativeFrameCreated;
    }

    internal async Task InitializeAsync()
    {
        await CallAsync("Page.enable", "{}");
        await CallAsync("Network.enable", "{}");
        await CallAsync("Network.setBypassServiceWorker", "{\"bypass\":true}");
        await CallAsync("Network.setCacheDisabled", "{\"cacheDisabled\":true}");
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

    private void NativeFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            var frame = e.Frame;
            if (_nativeFrames.ContainsKey(frame)) return;
            // Destroyed does not require a usable sender/native frame. Capture
            // identity and remove it without calling APIs on the destroyed object.
            EventHandler<object> destroyed = (_, _) => _nativeFrames.Remove(frame);
            _nativeFrames.Add(frame, destroyed);
            // FrameCreated precedes this frame's navigation. Subscribe recursively:
            // root CDP interception does not follow out-of-process child targets.
            e.Frame.NavigationStarting += NativeFrameNavigationStarting;
            e.Frame.FrameCreated += NativeFrameCreated;
            e.Frame.Destroyed += destroyed;
        }
        catch (Exception) { FailClosed(); }
    }

    private void NativeFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Never undo another host handler's cancellation. Redirects raise this
        // event again and must be evaluated again, regardless of navigation ID.
        if (e.Cancel) return;
        e.Cancel = true;
        if (_disposed || _faulted || !_ready) return;
        try
        {
            // Source is the native top-level document, not a child-controlled
            // Referer, parent message, or cached CDP frame-tree observation.
            e.Cancel = _navigation.EvaluateDocumentRequest(e.Uri, isMainFrame: false, _core.Source)
                is not NavigationDecision.Allowed;
        }
        catch (Exception) { FailClosed(); }
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
        string? networkId = null;
        try
        {
            using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
            var item = json.RootElement;
            var requestId = item.GetProperty("requestId").GetString();
            networkId = item.TryGetProperty("networkId", out var network) ? network.GetString() : null;
            if (networkId is not null && _cancelledDocuments.Contains(networkId)) return;
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
        catch (Exception)
        {
            // Native redirect cancellation can retire a request while its Fetch
            // event/command is still in flight. Only Chromium's terminal cancellation
            // for this exact network request proves there is nothing left to resume.
            // Uncorrelated errors remain fatal to the protection channel.
            if (!_disposed && (networkId is null || !_cancelledDocuments.Contains(networkId))) FailClosed();
        }
    }

    private void NetworkLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _faulted) return;
        try
        {
            using var data = JsonDocument.Parse(e.ParameterObjectAsJson);
            var item = data.RootElement;
            if (item.GetProperty("type").GetString() != "Document" ||
                !item.TryGetProperty("canceled", out var cancelled) || !cancelled.GetBoolean()) return;
            var id = item.GetProperty("requestId").GetString();
            if (string.IsNullOrEmpty(id)) { FailClosed(); return; }
            if (_cancelledDocuments.Add(id)) _cancelledOrder.Enqueue(id);
            // Retain recent terminal IDs as late Fetch events may arrive after the
            // cancellation. These are opaque IDs, never URLs or authentication data.
            while (_cancelledOrder.Count > 256) _cancelledDocuments.Remove(_cancelledOrder.Dequeue());
        }
        catch (Exception) { FailClosed(); }
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
        _core.FrameNavigationStarting -= NativeFrameNavigationStarting;
        _core.FrameCreated -= NativeFrameCreated;
        foreach (var (frame, destroyed) in _nativeFrames)
        {
            frame.NavigationStarting -= NativeFrameNavigationStarting;
            frame.FrameCreated -= NativeFrameCreated;
            frame.Destroyed -= destroyed;
        }
        _nativeFrames.Clear();
        _requests.DevToolsProtocolEventReceived -= RequestPaused;
        _frames.DevToolsProtocolEventReceived -= FrameNavigated;
        _networkFailures.DevToolsProtocolEventReceived -= NetworkLoadingFailed;
        // Do not Fetch.disable: it could resume pending requests before controller disposal.
    }
}
