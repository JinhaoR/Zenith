using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Zenith.Core.Permissions;

namespace Zenith.App.Navigation;

/// <summary>Applies Core's fixed file-selection decision to every renderer target before it runs.</summary>
internal sealed class FileChooserGuard : IDisposable
{
    private const string AutoAttach = "{\"autoAttach\":true,\"waitForDebuggerOnStart\":true,\"flatten\":true,\"filter\":[{\"type\":\"iframe\"},{\"exclude\":true}]}";
    private readonly CoreWebView2 _core;
    private readonly Func<CapabilityDecision> _evaluate;
    private readonly Action<string> _notify;
    private readonly Action _failed;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _attached;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _detached;
    private readonly CoreWebView2DevToolsProtocolEventReceiver _chooser;
    private readonly Dictionary<string, string> _sessions = [];
    private bool _disposed;
    private bool _faulted;

    internal FileChooserGuard(CoreWebView2 core, Func<CapabilityDecision> evaluate, Action<string> notify, Action failed)
    {
        _core = core;
        _evaluate = evaluate;
        _notify = notify;
        _failed = failed;
        _attached = core.GetDevToolsProtocolEventReceiver("Target.attachedToTarget");
        _detached = core.GetDevToolsProtocolEventReceiver("Target.detachedFromTarget");
        _chooser = core.GetDevToolsProtocolEventReceiver("Page.fileChooserOpened");
        _attached.DevToolsProtocolEventReceived += Attached;
        _detached.DevToolsProtocolEventReceived += Detached;
        _chooser.DevToolsProtocolEventReceived += ChooserOpened;
    }

    internal async Task InitializeAsync()
    {
        try { await ConfigureAsync(null); }
        catch { FailClosed(); throw; }
    }

    private async Task ConfigureAsync(string? session)
    {
        // Install cancellation before evaluating policy. There are no mutable
        // capability grants today; future grants must explicitly update this gate.
        await Call(session, "Page.setInterceptFileChooserDialog", "{\"enabled\":true,\"cancel\":true}");
        var decision = _evaluate();
        if (decision is null || string.IsNullOrWhiteSpace(decision.Explanation))
            throw new InvalidOperationException("File-selection policy unavailable.");
        if (decision.Allowed)
            // Leave Chromium's actual user-controlled chooser intact. Never supply
            // a path or use DOM.setFileInputFiles as a production grant mechanism.
            await Call(session, "Page.setInterceptFileChooserDialog", "{\"enabled\":false}");
        await Call(session, "Page.enable", "{}");
        // Auto-attach is not recursive; install it in each child target before
        // resuming that target so newly created nested OOPIFs cannot race setup.
        await Call(session, "Target.setAutoAttach", AutoAttach);
        if (session is not null && !_disposed && !_faulted && _sessions.ContainsKey(session))
            await Call(session, "Runtime.runIfWaitingForDebugger", "{}");
    }

    private async void Attached(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _faulted) return;
        string? session = null;
        var registered = false;
        try
        {
            using var data = JsonDocument.Parse(e.ParameterObjectAsJson);
            var root = data.RootElement;
            // Worker targets have no HTML chooser and are intentionally outside
            // this guard. Do not confuse a second diagnostic session on an already
            // guarded target with a newly discovered, unprotected renderer.
            if (root.GetProperty("targetInfo").GetProperty("type").GetString() != "iframe") return;
            var target = root.GetProperty("targetInfo").GetProperty("targetId").GetString();
            if (string.IsNullOrEmpty(target)) throw new InvalidOperationException("Missing iframe target.");
            if (!root.GetProperty("waitingForDebugger").GetBoolean())
            {
                if (_sessions.ContainsValue(target)) return;
                throw new InvalidOperationException("Iframe was not paused for file-selection policy.");
            }
            session = root.GetProperty("sessionId").GetString();
            if (string.IsNullOrEmpty(session)) throw new InvalidOperationException("Missing iframe session.");
            if (!_sessions.TryAdd(session, target)) return;
            registered = true;
            await ConfigureAsync(session);
        }
        catch (Exception)
        {
            // A target destroyed during setup needs no permission. Any other
            // transport/policy failure leaves it paused and destroys the host.
            if (!registered || _sessions.ContainsKey(session!)) FailClosed();
        }
    }

    private void Detached(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed) return;
        try
        {
            using var data = JsonDocument.Parse(e.ParameterObjectAsJson);
            var session = data.RootElement.GetProperty("sessionId").GetString();
            if (string.IsNullOrEmpty(session)) throw new InvalidOperationException("Missing detached session.");
            _sessions.Remove(session);
        }
        catch (Exception) { FailClosed(); }
    }

    private void ChooserOpened(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        if (_disposed || _faulted) return;
        try { _notify(_evaluate().Explanation); }
        catch (Exception) { FailClosed(); }
    }

    private Task<string> Call(string? session, string method, string parameters) =>
        (session is null ? _core.CallDevToolsProtocolMethodAsync(method, parameters)
            : _core.CallDevToolsProtocolMethodForSessionAsync(session, method, parameters))
        .WaitAsync(TimeSpan.FromSeconds(10));

    private void FailClosed()
    {
        if (_disposed || _faulted) return;
        _faulted = true;
        try { _core.Stop(); } catch (Exception) { /* Disposal is the final boundary. */ }
        _failed();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _attached.DevToolsProtocolEventReceived -= Attached;
        _detached.DevToolsProtocolEventReceived -= Detached;
        _chooser.DevToolsProtocolEventReceived -= ChooserOpened;
        // Do not disable interception/auto-attach or resume paused targets while
        // controller disposal is pending.
        _sessions.Clear();
    }
}
