using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Zenith.App.Filtering;

/// <summary>Frame-scoped, read-only cosmetic bridge. No policy services are reachable here.</summary>
internal sealed class CosmeticFilterGuard : IDisposable
{
    private readonly CoreWebView2 _core;
    private readonly Func<string, string[], string[], string[], string> _cosmetics;
    private readonly Dictionary<CoreWebView2Frame, FrameSubscription> _frames = [];
    private readonly MessageBudget _mainBudget = new();
    private string? _scriptId;
    private bool _disposed;
    private long _windowStart;
    private int _windowMessages;
    private sealed class MessageBudget { internal long Last; }
    private sealed record FrameSubscription(EventHandler<CoreWebView2WebMessageReceivedEventArgs> Message,
        EventHandler<object> Destroyed);

    internal CosmeticFilterGuard(CoreWebView2 core, Func<string, string[], string[], string[], string> cosmetics)
    {
        _core = core;
        _cosmetics = cosmetics;
        core.WebMessageReceived += MainMessage;
        core.FrameCreated += FrameCreated;
    }

    internal async Task InitializeAsync()
    {
        var id = await _core.AddScriptToExecuteOnDocumentCreatedAsync(AdblockEngine.ReadAsset("cosmetics.js"));
        if (_disposed) _core.RemoveScriptToExecuteOnDocumentCreated(id);
        else _scriptId = id;
    }

    private void MainMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (_disposed || !CosmeticMessage.IsCurrentDocument(e.Source, _core.Source)) return;
            Receive(e, _mainBudget, _core.PostWebMessageAsJson);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // A controller closing during delivery cannot receive a cosmetic reply.
        }
    }

    private void FrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        var frame = e.Frame;
        if (_disposed || _frames.ContainsKey(frame)) return;
        var budget = new MessageBudget();
        EventHandler<CoreWebView2WebMessageReceivedEventArgs> message = (_, args) => Receive(args, budget, frame.PostWebMessageAsJson);
        // WebView2 has already torn down the native frame when Destroyed fires.
        // Calling event-removal APIs at that point can crash the native runtime.
        EventHandler<object> destroyed = (_, _) => _frames.Remove(frame);
        _frames.Add(frame, new(message, destroyed));
        frame.WebMessageReceived += message;
        frame.Destroyed += destroyed;
        frame.FrameCreated += FrameCreated;
    }

    private void Receive(CoreWebView2WebMessageReceivedEventArgs e, MessageBudget budget, Action<string> reply)
    {
        if (_disposed) return;
        // A page can forge messages. Limit work and use WebView2's actual sender URL,
        // never a page-supplied origin, when choosing cosmetic exceptions.
        var now = Stopwatch.GetTimestamp();
        if (budget.Last != 0 && Stopwatch.GetElapsedTime(budget.Last, now) < TimeSpan.FromMilliseconds(400)) return;
        budget.Last = now;
        if (_windowStart == 0 || Stopwatch.GetElapsedTime(_windowStart, now) >= TimeSpan.FromSeconds(1))
        {
            _windowStart = now;
            _windowMessages = 0;
        }
        if (++_windowMessages > 32) return;
        try
        {
            var query = CosmeticMessage.Read(e.WebMessageAsJson, e.Source);
            if (query is null) return;
            var css = _cosmetics(e.Source, query.Classes, query.Ids, query.Hrefs);
            if (css.Length > 2 * 1024 * 1024) return;
            reply(JsonSerializer.Serialize(new { kind = "zenith-cosmetics", token = query.Token, url = e.Source, css }));
        }
        catch (Exception ex) when (ex is JsonException or COMException or InvalidOperationException or ArgumentException) { }
    }

    private void Detach(CoreWebView2Frame frame)
    {
        if (!_frames.Remove(frame, out var subscription)) return;
        frame.WebMessageReceived -= subscription.Message;
        frame.Destroyed -= subscription.Destroyed;
        frame.FrameCreated -= FrameCreated;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _core.WebMessageReceived -= MainMessage;
        _core.FrameCreated -= FrameCreated;
        foreach (var frame in _frames.Keys.ToArray()) Detach(frame);
        if (_scriptId is not null) _core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
    }
}
