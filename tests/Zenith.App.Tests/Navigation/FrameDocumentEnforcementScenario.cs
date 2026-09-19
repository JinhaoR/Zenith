using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.Core.Filtering;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class FrameDocumentEnforcementScenario
{
    internal static async Task RunAsync()
    {
        await RunAsync(nativeOnly: false);
        await RunAsync(nativeOnly: true);
    }

    private sealed class PolicySource : ISitePolicySource
    {
        public SitePolicySnapshot? Snapshot { get; set; }
        public bool TryGetActivePolicy([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SitePolicySnapshot? snapshot) { snapshot = Snapshot; return snapshot is not null; }
    }

    private sealed class Blacklist : IBlacklistSource
    {
        public HostsBlacklist Current { get; } = HostsBlacklist.Parse("0.0.0.0 blocked.zenith.test");
    }

    private static async Task RunAsync(bool nativeOnly)
    {
        await using var top = new LoopbackSite("127.0.0.1");
        await using var child = new LoopbackSite("127.0.0.2");
        await using var denied = new LoopbackSite("127.0.0.3");
        await using var grey = new LoopbackSite("127.0.0.4");
        foreach (var site in new[] { top, child, denied, grey })
            site.Response = path =>
            {
                if (path.StartsWith("/redirect/"))
                {
                    var parts = path.Split('/');
                    var sink = parts[3] == "denied" ? denied : grey;
                    return (int.Parse(parts[2]), "Location: " + sink.Origin + "/sink/" + parts[4] + "\r\n", "");
                }
                return (200, "Cache-Control: no-store\r\n",
                    "<html><body>Frame fixture<script>window.fixture=true;window.reports=[];addEventListener('message',e=>reports.push({origin:e.origin,data:e.data}));parent.postMessage({loaded:location.href},'*');</script></body></html>");
            };
        // Independent positive controls: an unreachable fixture must not look secure.
        using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
            foreach (var site in new[] { top, child, denied, grey })
                Assert.True((await http.GetAsync(site.Origin + "/control")).IsSuccessStatusCode);

        var blacklist = new Blacklist();
        var policy = new PolicySource
        {
            Snapshot = new SitePolicySnapshot(
                [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist),
                 new SitePolicyEntry("127.0.0.2", AccessClass.Whitelist),
                 new SitePolicyEntry("127.0.0.3", AccessClass.Blacklist),
                 new SitePolicyEntry("blocked.zenith.test", AccessClass.Whitelist)],
                mandatoryBlacklist: blacklist.Current)
        };
        var coordinator = new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy));
        var folder = Directory.CreateTempSubdirectory("Zenith-FramePolicy-");
        var window = new MainWindow(coordinator, blacklist: blacklist)
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            await Until(() => Task.FromResult((bool)tab.GetType().GetProperty("IsReady")!.GetValue(tab)!));
            if (nativeOnly)
                // Test-only removal of root Fetch interception proves that the native
                // frame boundary is independently effective. Production retains Fetch.
                await core.CallDevToolsProtocolMethodAsync("Fetch.disable", "{}");
            Console.WriteLine($"F02 nativeOnly={nativeOnly}, runtime={core.Environment.BrowserVersionString}");

            var frames = new Dictionary<string, CoreWebView2Frame>();
            var navigations = new List<(string Uri, bool Cancel, bool Redirect)>();
            void Created(object? sender, CoreWebView2FrameCreatedEventArgs e)
            {
                var frame = e.Frame;
                frames[frame.Name] = frame;
                frame.NavigationStarting += (_, args) => navigations.Add((args.Uri, args.Cancel, args.IsRedirected));
                frame.FrameCreated += Created;
            }
            core.FrameCreated += Created;
            Request(window, top.Origin + "/top");
            await Until(async () => core.Source == top.Origin + "/top" && await core.ExecuteScriptAsync("window.fixture===true") == "true");
            foreach (var (name, url) in new[] { ("same", top.Origin + "/same"), ("oopif", child.Origin + "/oopif") })
            {
                await core.ExecuteScriptAsync("(()=>{const f=document.createElement('iframe');f.name=" + J(name) + ";f.src=" + J(url) + ";document.body.append(f)})()");
                await Until(async () => frames.TryGetValue(name, out var frame) && await frame.ExecuteScriptAsync("window.fixture===true") == "true");
            }
            var processFrames = (await core.Environment.GetProcessExtendedInfosAsync())
                .SelectMany(p => p.AssociatedFrameInfos.Select(f => new { pid = p.ProcessInfo.ProcessId, f.FrameId, f.Source })).ToArray();
            var topPid = processFrames.Single(f => f.Source == top.Origin + "/top").pid;
            var oopifPid = processFrames.Single(f => f.FrameId == frames["oopif"].FrameId).pid;
            Assert.NotEqual(topPid, oopifPid);
            Assert.Equal(topPid, processFrames.Single(f => f.FrameId == frames["same"].FrameId).pid);
            Console.WriteLine($"F02 verified renderer PIDs: root={topPid}, oopif={oopifPid}");

            var contexts = new (string Name, Func<string, Task<string>> Execute)[]
            {
                ("root", core.ExecuteScriptAsync),
                ("same", frames["same"].ExecuteScriptAsync),
                ("oopif", frames["oopif"].ExecuteScriptAsync)
            };
            foreach (var (name, execute) in contexts)
            {
                var blockedUrl = denied.Origin + "/direct/" + name;
                await execute(AppendFrame("direct-" + name, blockedUrl));
                await Until(() => Task.FromResult(navigations.Any(n => n.Uri == blockedUrl && n.Cancel)));
                await Task.Delay(100);
                Assert.Equal("false", await execute("reports.some(r=>r.origin===" + J(denied.Origin) + "&&r.data.loaded===" + J(blockedUrl) + ")"));

                var allowedUrl = grey.Origin + "/compatible/" + name;
                Assert.IsType<NavigationDecision.Allowed>(coordinator.EvaluateDocumentRequest(allowedUrl, false, core.Source));
                await execute(AppendFrame("compatible-" + name, allowedUrl));
                await Until(async () => await execute("reports.some(r=>r.origin===" + J(grey.Origin) + "&&r.data.loaded===" + J(allowedUrl) + ")") == "true");
                Assert.Contains("/compatible/" + name, grey.Requests);

                foreach (var status in new[] { 301, 302, 303, 307, 308 })
                {
                    foreach (var outcome in new[] { "denied", "allowed" })
                    {
                        var id = name + "-" + status + "-" + outcome;
                        var action = (name == "oopif" ? child : top).Origin + "/redirect/" + status + "/" + outcome + "/" + id;
                        var sink = (outcome == "denied" ? denied : grey).Origin + "/sink/" + id;
                        await execute("(()=>{const frame=document.createElement('iframe');frame.name=" + J(id) + ";document.body.append(frame);const f=document.createElement('form');f.target=frame.name;f.method='POST';f.action=" + J(action) + ";f.innerHTML='<input name=password value=fixture-password><input name=token value=fixture-token>';document.body.append(f);f.submit()})()");
                        await Until(() => Task.FromResult((name == "oopif" ? child : top).Received.Any(r => r.Path == new Uri(action).PathAndQuery && r.Body.Contains("fixture-password"))));
                        await Until(() => Task.FromResult(navigations.Any(n => n.Uri == sink && n.Redirect && n.Cancel == (outcome == "denied"))));
                        if (outcome == "denied")
                        {
                            await Task.Delay(100);
                            Assert.Equal("false", await execute("reports.some(r=>r.data.loaded===" + J(sink) + ")"));
                            if (status is 307 or 308) Assert.DoesNotContain(denied.Received, r => r.Path == "/sink/" + id);
                        }
                        else
                        {
                            await Until(async () => await execute("reports.some(r=>r.data.loaded===" + J(sink) + ")") == "true");
                            var received = Assert.Single(grey.Received, r => r.Path == "/sink/" + id);
                            Assert.Equal(status is 307 or 308 ? "POST" : "GET", received.Method);
                            Assert.Equal(status is 307 or 308 ? "password=fixture-password&token=fixture-token" : "", received.Body);
                        }
                    }
                }
            }
            // Keep the independent observer alive after all cancelled completions.
            await Task.Delay(300);
            Assert.DoesNotContain(denied.Received, r => r.Method == "POST" || r.Body.Contains("fixture-"));

            const string mandatoryTarget = "https://blocked.zenith.test/mandatory";
            Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.Blacklisted),
                coordinator.EvaluateDocumentRequest(mandatoryTarget, false, core.Source));
            await frames["oopif"].ExecuteScriptAsync(AppendFrame("mandatory", mandatoryTarget));
            await Until(() => Task.FromResult(navigations.Any(n => n.Uri == mandatoryTarget && n.Cancel)));

            // Re-evaluate current Core state, rather than granting a frame lifetime
            // authorization from its parent's creation time.
            var snapshot = policy.Snapshot;
            policy.Snapshot = null;
            var unavailable = child.Origin + "/unavailable-policy";
            await frames["oopif"].ExecuteScriptAsync(AppendFrame("unavailable", unavailable));
            await Until(() => Task.FromResult(navigations.Any(n => n.Uri == unavailable && n.Cancel)));
            policy.Snapshot = snapshot;
            await frames["oopif"].ExecuteScriptAsync("document.querySelectorAll('iframe').forEach(f=>f.remove())");
            await frames["oopif"].ExecuteScriptAsync(AppendFrame("after-destruction", grey.Origin + "/after-destruction"));
            await Until(async () => await frames["oopif"].ExecuteScriptAsync("reports.some(r=>r.data.loaded===" + J(grey.Origin + "/after-destruction") + ")") == "true");
            Console.WriteLine($"F02 passed nativeOnly={nativeOnly}: denied documents did not execute; no denied POST/body; all five redirect statuses and allowed embedded forms verified.");
        }
        finally
        {
            window.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string AppendFrame(string name, string url) =>
        "(()=>{const f=document.createElement('iframe');f.name=" + J(name) + ";f.src=" + J(url) + ";document.body.append(f)})()";
    private static string J(string text) => JsonSerializer.Serialize(text);
    private static void Request(MainWindow window, string target) => typeof(MainWindow)
        .GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [target, NavigationOrigin.AddressBar]);
    private static async Task Until(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 500; i++) { if (await condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Frame-document regression state did not arrive.");
    }
}
