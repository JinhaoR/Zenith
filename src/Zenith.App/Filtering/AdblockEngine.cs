using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Zenith.Core.Filtering;

namespace Zenith.App.Filtering;

/// <summary>Pinned code, data-only inputs, and no CLR objects exposed to JavaScript.</summary>
internal sealed class AdblockEngine : IResourceFilterEngine, IDisposable
{
    private readonly V8ScriptEngine _runtime;
    private readonly ScriptObject _api;
    private readonly object _gate = new();
    private readonly Stopwatch _execution = new();
    private TimeSpan _budget;
    internal int NetworkRuleCount { get; }
    internal int CosmeticRuleCount { get; }

    internal AdblockEngine(string rules)
    {
        _runtime = new V8ScriptEngine(new V8RuntimeConstraints { MaxOldSpaceSize = 1024, MaxArrayBufferAllocation = (UIntPtr)(256 * 1024 * 1024) })
        {
            MaxRuntimeHeapSize = (UIntPtr)(512 * 1024 * 1024),
            MaxRuntimeStackUsage = (UIntPtr)(1024 * 1024),
            AllowReflection = false,
        };
        _runtime.ContinuationCallback = () => _execution.Elapsed < _budget;
        try
        {
            StartBudget(TimeSpan.FromSeconds(30));
            _runtime.Execute(ReadAsset("engine.js"));
            _api = (ScriptObject)_runtime.Evaluate("ZenithAdblock");
            _api.InvokeMethod("load", rules);
            using var counts = JsonDocument.Parse((string)_api.InvokeMethod("counts"));
            NetworkRuleCount = counts.RootElement.GetProperty("network").GetInt32();
            CosmeticRuleCount = counts.RootElement.GetProperty("cosmetic").GetInt32();
        }
        catch { _runtime.Dispose(); throw; }
    }

    public bool Blocks(ResourceRequest request)
    {
        if (request.Url.Length > 32768 || request.SourceUrl.Length > 32768) return true;
        lock (_gate)
        {
            StartBudget(TimeSpan.FromMilliseconds(250));
            return (bool)_api.InvokeMethod("blocks", JsonSerializer.Serialize(new
            {
                url = request.Url, sourceUrl = request.SourceUrl, type = RequestType(request.Kind)
            }));
        }
    }

    internal string Cosmetics(string url, string[] classes, string[] ids, string[] hrefs)
    {
        lock (_gate)
        {
            StartBudget(TimeSpan.FromMilliseconds(250));
            return (string)_api.InvokeMethod("cosmetics", JsonSerializer.Serialize(new { url, classes, ids, hrefs }));
        }
    }

    private void StartBudget(TimeSpan budget) { _budget = budget; _execution.Restart(); }

    private static string RequestType(ResourceKind kind) => kind switch
    {
        ResourceKind.Document => "main_frame", ResourceKind.Frame => "sub_frame",
        ResourceKind.Script => "script", ResourceKind.Stylesheet => "stylesheet",
        ResourceKind.Image => "image", ResourceKind.Font => "font", ResourceKind.Media => "media",
        ResourceKind.Fetch => "xmlhttprequest", ResourceKind.Ping => "ping", ResourceKind.WebSocket => "websocket",
        _ => "other"
    };

    internal static string ReadAsset(string name)
    {
        using var input = typeof(AdblockEngine).Assembly.GetManifestResourceStream("Zenith.App.Filtering.Assets." + name)
            ?? throw new IOException("Missing filtering asset.");
        using var reader = new StreamReader(input);
        return reader.ReadToEnd();
    }

    public void Dispose() { lock (_gate) { _api.Dispose(); _runtime.Dispose(); } }
}
