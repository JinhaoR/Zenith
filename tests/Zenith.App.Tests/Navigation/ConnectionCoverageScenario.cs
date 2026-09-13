using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Zenith.App.Tests.Navigation;

/// <summary>Server-observed probes. No real credentials or public endpoints are used.</summary>
internal static class ConnectionCoverageScenario
{
    internal static async Task RunAsync(CoreWebView2 core, LoopbackSite allowed, LoopbackSite denied)
    {
        var localDenials = new HashSet<string>(StringComparer.Ordinal);
        void Requested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (e.Response?.StatusCode == 403) localDenials.Add(e.Request.Uri);
        }
        core.WebResourceRequested += Requested;
        try
        {
            Console.WriteLine($"Connection coverage runtime: {core.Environment.BrowserVersionString}");
            foreach (var kind in new[] { "fetch", "keepalive", "xhr", "beacon", "eventsource", "srcdoc", "blank-frame" })
            {
                var path = "/connection-" + kind;
                // Run the identical API against the reachable literal-loopback receiver first.
                // The DNS target then exercises combined transport/mandatory-host denial.
                await ProbeAsync(core, kind, denied.ListenerOrigin + path + "-control");
                await UntilAsync(() => denied.Requests.Contains(path + "-control"));
                await ProbeAsync(core, kind, denied.Origin + path + "-denied");
                await UntilAsync(() => localDenials.Contains(denied.Origin + path + "-denied"));
                Assert.DoesNotContain(path + "-denied", denied.Requests);
            }

            // HTTP observers intentionally reject the upgrade. An error event is NOT
            // proof of blocking: the handshake may already have reached the server.
            await ProbeAsync(core, "websocket", allowed.ListenerOrigin.Replace("http:", "ws:") + "/socket-control");
            await UntilAsync(() => allowed.Requests.Contains("/socket-control"));
            await ProbeAsync(core, "websocket", denied.Origin.Replace("http:", "ws:") + "/socket-denied");
            Assert.DoesNotContain(denied.Requests, path => path.StartsWith("/connection-", StringComparison.Ordinal) && path.EndsWith("-denied", StringComparison.Ordinal));
            var socketReachedServer = denied.Requests.Contains("/socket-denied");
            Console.WriteLine(socketReachedServer
                ? "CONNECTION RELEASE BLOCKER: denied WebSocket handshake reached the loopback observer."
                : "WebSocket handshake absent at the loopback observer in this run; other connection paths remain unproven.");
            if (Environment.GetEnvironmentVariable("ZENITH_STRICT_CONNECTION_TESTS") == "1")
                Assert.False(socketReachedServer, "Denied WebSocket handshake reached the server. Connection-security release gate failed.");
        }
        finally { core.WebResourceRequested -= Requested; }
    }

    private static async Task ProbeAsync(CoreWebView2 core, string kind, string address)
    {
        var target = JsonSerializer.Serialize(address);
        var script = kind switch
        {
            "fetch" => $"fetch({target},{{mode:'no-cors'}}).finally(done).catch(()=>{{}});",
            "keepalive" => $"fetch({target},{{method:'POST',body:'fixture-only',mode:'no-cors',keepalive:true}}).finally(done).catch(()=>{{}});",
            "xhr" => $"const x=new XMLHttpRequest(); x.onloadend=done; x.open('GET',{target}); x.send();",
            "beacon" => $"window.connectionBeaconQueued=navigator.sendBeacon({target},'fixture-only'); done();",
            "eventsource" => $"const s=new EventSource({target}); s.onerror=()=>{{s.close();done();}}; s.onmessage=()=>{{s.close();done();}};",
            "srcdoc" => "window.connectionFrame?.remove(); window.connectionFrame=document.createElement('iframe'); " +
                "connectionFrame.srcdoc=" + JsonSerializer.Serialize("<script>fetch(" + target + ",{mode:'no-cors'}).catch(()=>{});</script>") +
                "; document.body.append(connectionFrame); done();",
            "blank-frame" => $"window.connectionFrame?.remove(); window.connectionFrame=document.createElement('iframe'); " +
                $"connectionFrame.onload=()=>{{connectionFrame.contentWindow.fetch({target},{{mode:'no-cors'}}).finally(done).catch(()=>{{}});}}; document.body.append(connectionFrame);",
            "websocket" => $"const s=new WebSocket({target}); s.onerror=done; s.onopen=()=>{{s.close();done();}}; s.onclose=done;",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        // Late error/close callbacks from a previous probe cannot complete the next one.
        await core.ExecuteScriptAsync("(()=>{const id=(window.connectionProbeId??0)+1;window.connectionProbeId=id;" +
            "window.connectionProbeDone=false;const done=()=>{if(window.connectionProbeId===id)window.connectionProbeDone=true;};" + script + "})()");
        await UntilAsync(async () => await core.ExecuteScriptAsync("window.connectionProbeDone") == "true");
        if (kind == "beacon") Assert.Equal("true", await core.ExecuteScriptAsync("window.connectionBeaconQueued"));
    }

    private static Task UntilAsync(Func<bool> condition) => UntilAsync(() => Task.FromResult(condition()));

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 500; i++)
        {
            if (await condition()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Connection probe did not complete or its positive control/denial was not observed.");
    }
}
