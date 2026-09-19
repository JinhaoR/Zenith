using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.App.Filtering;
using Zenith.App.Tests.Navigation;
using Zenith.Core.Navigation;
using Zenith.Core.Filtering;
using Zenith.Core.Permissions;

internal static class UbolExperiment
{
    private static readonly List<object> Evidence = [];
    private static string _output = "";
    private static void Record(string name, object? value)
    {
        Evidence.Add(new { name, value });
        Console.WriteLine(name + ": " + JsonSerializer.Serialize(value));
        File.WriteAllText(_output, JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { WriteIndented = true }));
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("extension-folder|none profile-folder output.json mode [sites] [runtime-folder]"); return 2; }
        _output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(Path.GetDirectoryName(_output)!);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var result = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await Run(args); }
            catch (Exception ex) { Record("fatal", ex.ToString()); result = 1; }
            finally { app.Shutdown(); }
        });
        app.Run();
        return result;
    }
    private sealed class Blacklist : IBlacklistSource
    {
        public HostsBlacklist Current { get; } = HostsBlacklist.Parse("0.0.0.0 blocked.zenith.test");
    }
    private static async Task Run(string[] args)
    {
        var mode = args[3];
        var profile = Path.GetFullPath(args[1]);
        var watch = Stopwatch.StartNew();
        var options = new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = mode != "extensions-off", AdditionalBrowserArguments = "--no-proxy-server" };
        var environment = await CoreWebView2Environment.CreateAsync(args.Length > 5 ? args[5] : null, profile, options);
        var browser = new WebView2();
        var host = new Window { Content = browser, Width = 1280, Height = 900, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
        var guards = new List<IDisposable>();
        await using var fixture = new LoopbackSite("127.0.0.1", "localhost");
        var listVersion = 1;
        fixture.Response = path => path.StartsWith("/filters.txt")
            ? (200, "Access-Control-Allow-Origin: *\r\n", "[Adblock Plus 2.0]\n! Title: Zenith isolated test\n! Expires: 1 hours\n||localhost^*/custom-block-" + listVersion + "\nlocalhost###custom-ad\n")
            : path.StartsWith("/page") ? (200, "", """
                <html><head><title>Zenith extension fixture</title></head><body>
                <div id="ad-banner-1">Generic ad</div><div id="ccf1"><b class="fail">Specific ad</b></div>
                <div id="custom-ad">Custom ad</div><div id="control">Visible control</div>
                <script>window.sf1Sentinel=true;window.fixture=true;</script>
                <script src="/bnf1.js"></script><script src="/bnf3.js"></script><script src="/xpopup/xpopup.js"></script>
                <script src="/analytics/rakuten.js"></script>
                <script src="/custom-block-1"></script><script src="/custom-block-2"></script>
                <img src="/bnf2.png"><img src="/control.png"></body></html>
                """) : (200, "Access-Control-Allow-Origin: *\r\n", "");
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            Record("startup", new { runtime = environment.BrowserVersionString, milliseconds = watch.ElapsedMilliseconds, mode, extension = args[0] });
            Record("memory-initial", Memory(environment));
            core.NewWindowRequested += (_, e) => { e.Handled = true; Record("popup-request", new { scheme = new Uri(e.Uri).Scheme }); };
            core.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            var existing = await core.Profile.GetBrowserExtensionsAsync();
            Record("extensions-before", existing.Select(e => new { e.Id, e.Name, e.IsEnabled }));
            CoreWebView2BrowserExtension? extension = existing.FirstOrDefault(e => e.Name.Contains("uBlock", StringComparison.OrdinalIgnoreCase));
            if (args[0] != "none" && (extension is null || mode == "reinstall"))
            {
                watch.Restart();
                try
                {
                    extension = await core.Profile.AddBrowserExtensionAsync(Path.GetFullPath(args[0]));
                    Record("installed", new { extension.Id, extension.Name, extension.IsEnabled, milliseconds = watch.ElapsedMilliseconds });
                }
                catch (Exception ex) { Record("install-error", new { ex.Message, ex.HResult }); }
            }
            if (extension is not null && mode != "measure")
            {
                var dashboard = "chrome-extension://" + extension.Id + "/dashboard.html";
                Record("dashboard-navigation", await Navigate(core, dashboard));
                Record("extension-api", await Eval(core, "(async()=>({id:chrome.runtime.id,version:chrome.runtime.getManifest().version,permissions:await chrome.permissions.getAll(),dnr:typeof chrome.declarativeNetRequest,scripting:typeof chrome.scripting,userScripts:typeof chrome.userScripts,offscreen:typeof chrome.offscreen,update:typeof chrome.runtime.requestUpdateCheck}))()"));
                Record("dashboard-state", await Eval(core, "chrome.runtime.sendMessage({what:'getOptionsPageData'}).then(d=>({mode:d.defaultFilteringMode,enabled:d.enabledRulesets,hasOmnipotence:d.hasOmnipotence,supportsCompiledFilters:d.supportsCompiledFilters,supportsUserScripts:d.supportsUserScripts}))"));
                Record("persistence-marker", await Eval(core, "chrome.storage.local.get('zenithExperimentMarker')"));
                await Eval(core, "chrome.storage.local.set({zenithExperimentMarker:'isolated-fixture'})");
                if (mode != "restart" && mode != "reinstall")
                {
                    Record("mode-configured", await Eval(core, "chrome.runtime.sendMessage({what:'setDefaultFilteringMode',level:" + (mode == "basic" ? 1 : mode == "optimal" ? 2 : 3) + "})"));
                    Record("test-rules", await Eval(core, "(async()=>{const r=await chrome.declarativeNetRequest.getEnabledRulesets();if(!r.includes('ubol-tests'))r.push('ubol-tests');await chrome.runtime.sendMessage({what:'applyRulesets',enabledRulesets:r});return await chrome.declarativeNetRequest.getEnabledRulesets()})()"));
                    Record("custom-list-import", await Eval(core, "chrome.runtime.sendMessage({what:'importFilterList',url:" + J(fixture.Origin + "/filters.txt") + "})"));
                    Record("custom-scriptlet", await Eval(core, "chrome.runtime.sendMessage({what:'addCustomFilters',hostname:'localhost',selectors:['+js(set-constant, customSentinel, true)']})"));
                    Record("registered-scripts", await Eval(core, "chrome.scripting.getRegisteredContentScripts().then(x=>x.map(s=>({id:s.id,world:s.world})))"));
                    Record("user-scripts", await Eval(core, "(async()=>{try{return await chrome.userScripts.getScripts()}catch(e){return {error:e.message}}})()"));
                    Record("extension-update-check", await Eval(core, "(async()=>{try{return await chrome.runtime.requestUpdateCheck()}catch(e){return {error:e.message}}})()"));
                }
                Record("dashboard-dom", await Eval(core, "({title:document.title,bodyCharacters:document.body.innerText.length,frames:document.querySelectorAll('iframe').length})"));
                if (mode == "inspect")
                {
                    Record("extensions-ui", await Navigate(core, "edge://extensions/"));
                    Record("popup-navigation", await Navigate(core, "chrome-extension://" + extension.Id + "/popup.html"));
                    Record("popup-state", await Eval(core, "(async()=>({text:document.body.innerText,tabs:(await chrome.tabs.query({active:true,currentWindow:true})).map(t=>({id:t.id,url:t.url}))}))()"));
                    await Navigate(core, dashboard);
                    Record("manual-custom-css", await Eval(core, "chrome.runtime.sendMessage({what:'addCustomFilters',hostname:'localhost',selectors:['#custom-ad']})"));
                }
            }
            if (mode is "zenith" or "combined")
            {
                var source = new Blacklist();
                var coordinator = new NavigationCoordinator(new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
                    [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist)], mandatoryBlacklist: source.Current))));
                void Failed() { Record("guard-failed", true); browser.Dispose(); }
                guards.Add(new NetworkSafetyGuard(core, Failed));
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.ServiceWorkers);
                var documents = new DocumentRequestGuard(core, coordinator, Failed);
                guards.Add(documents); await documents.InitializeAsync();
                AdblockService? adblock = null;
                if (mode == "combined") { adblock = new AdblockService(Path.Combine(profile, "ZenithFilters"), new HttpClient()); guards.Add(adblock); }
                guards.Add(new ResourceRequestGuard(core, source, adblock, Failed));
                var capabilities = new BrowserCapabilityGuard(core, new BrowserCapabilityPolicy(), _ => { }, Failed);
                guards.Add(capabilities); await capabilities.InitializeAsync(adblock is not null);
                if (adblock is not null) { var cosmetics = new CosmeticFilterGuard(core, adblock.Cosmetics); guards.Add(cosmetics); await cosmetics.InitializeAsync(); }
                core.NavigationStarting += (_, e) => { if (coordinator.EvaluateDocumentRequest(e.Uri, true, null) is not NavigationDecision.Allowed) e.Cancel = true; };
                Record("guards-attached", mode);
            }
            var fixtureOrigin = mode is "zenith" or "combined" ? fixture.ListenerOrigin : fixture.Origin;
            Record("fixture-navigation", await Navigate(core, fixtureOrigin + "/page"));
            await Task.Delay(1000);
            Record("fixture-dom", await Eval(core, "({generic:getComputedStyle(document.getElementById('ad-banner-1')).display,specific:getComputedStyle(document.querySelector('#ccf1 .fail')).display,custom:getComputedStyle(document.getElementById('custom-ad')).display,control:getComputedStyle(document.getElementById('control')).display,scriptlet:typeof sf1Sentinel,customScriptlet:window.customSentinel??null})"));
            Record("fixture-server", fixture.Requests.ToArray());
            Record("memory-fixture", Memory(environment));
            if (extension is not null && mode is not ("zenith" or "combined" or "measure"))
            {
                await Navigate(core, "chrome-extension://" + extension.Id + "/dashboard.html");
                listVersion = 2;
                // Isolated expiry simulation; no extension source or real profile changed.
                Record("simulate-list-expiry", await Eval(core, "(async()=>{const d=await chrome.storage.local.get('rulesets.imported');for(const l of d['rulesets.imported']??[])l.time.updated=0;await chrome.storage.local.set(d);return true})()"));
                Record("custom-list-update", await Eval(core, "chrome.runtime.sendMessage({what:'updateImportedLists'})"));
                Record("dynamic-rules", await Eval(core, "chrome.declarativeNetRequest.getDynamicRules().then(r=>({count:r.length,custom:r.filter(x=>JSON.stringify(x).includes('custom-block'))}))"));
                await extension.EnableAsync(false);
                await Task.Delay(500);
                Record("disabled", new { extension.IsEnabled });
                Record("disabled-fixture", await Navigate(core, fixture.Origin + "/page-disabled"));
                Record("disabled-dom", await Eval(core, "({generic:getComputedStyle(document.getElementById('ad-banner-1')).display,scriptlet:typeof sf1Sentinel})"));
                Record("disabled-server", fixture.Requests.ToArray());
                await extension.EnableAsync(true);
                await Task.Delay(500);
            }
            if (args.Length > 4 && args[4] == "sites")
            {
                foreach (var url in new[] { "https://en.wikipedia.org/wiki/Web_browser", "https://github.com/", "https://mail.google.com/", "https://www.youtube.com/", "https://www.reddit.com/", "https://www.speedtest.net/", "https://www.cnn.com/" })
                {
                    Record("public-site", new { url, result = await Navigate(core, url) });
                    Record("public-memory", new { host = new Uri(url).Host, memory = Memory(environment) });
                }
            }
            if (args.Length > 4 && args[4] == "youtube")
            {
                Record("youtube-initial", await Navigate(core, "https://www.youtube.com/"));
                await Task.Delay(10000);
                Record("youtube-settled", await Eval(core, "({title:document.title,bodyCharacters:document.body.innerText.length,ready:document.readyState,videoCount:document.querySelectorAll('video').length,consent:location.hostname.includes('consent'),navigation:performance.getEntriesByType('navigation').map(n=>({load:n.loadEventEnd,domContentLoaded:n.domContentLoadedEventEnd}))})"));
            }
            if (mode is "zenith" or "combined")
            {
                Record("core-denied-navigation", await Navigate(core, fixture.Origin + "/denied-by-transport"));
                Record("guarded-dashboard", await Navigate(core, "chrome-extension://" + extension!.Id + "/dashboard.html"));
            }
            if (mode.StartsWith("measure", StringComparison.Ordinal))
            {
                // Give the MV3 worker its ordinary inactivity interval, with no
                // dashboard, imported lists, or CDP worker attachment keeping it up.
                await Task.Delay(35000);
                Record("memory-idle-stock", Memory(environment));
            }
            Record("memory-final", Memory(environment));
        }
        finally
        {
            foreach (var guard in guards.AsEnumerable().Reverse()) guard.Dispose();
            browser.Dispose(); host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
    private static object Memory(CoreWebView2Environment environment)
    {
        long working = 0, privateBytes = 0; var count = 0;
        foreach (var info in environment.GetProcessInfos())
            try { using var p = Process.GetProcessById(info.ProcessId); working += p.WorkingSet64; privateBytes += p.PrivateMemorySize64; count++; } catch (ArgumentException) { }
        return new { processCount = count, workingBytes = working, privateBytes };
    }
    private static async Task<object> Navigate(CoreWebView2 core, string url)
    {
        var done = new TaskCompletionSource<(bool, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult((e.IsSuccess, e.WebErrorStatus.ToString()));
        core.NavigationCompleted += Completed;
        var watch = Stopwatch.StartNew();
        try
        {
            core.Navigate(url);
            var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(500);
            var dom = await Eval(core, "({title:document.title,bodyCharacters:document.body?.innerText.length??0,ready:document.readyState,resources:performance.getEntriesByType('resource').length,navigation:performance.getEntriesByType('navigation').map(n=>({domContentLoaded:n.domContentLoadedEventEnd,load:n.loadEventEnd}))})");
            return new { success = result.Item1, error = result.Item2, milliseconds = watch.ElapsedMilliseconds, scheme = new Uri(core.Source).Scheme, dom };
        }
        catch (Exception ex) { core.Stop(); return new { error = ex.GetType().Name, milliseconds = watch.ElapsedMilliseconds }; }
        finally { core.NavigationCompleted -= Completed; }
    }
    private static string J(object value) => JsonSerializer.Serialize(value);
    private static async Task<JsonElement> Eval(CoreWebView2 core, string expression)
    {
        try
        {
            using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", J(new { expression, awaitPromise = true, returnByValue = true, userGesture = true })).WaitAsync(TimeSpan.FromSeconds(15)));
            if (result.RootElement.TryGetProperty("exceptionDetails", out var exception)) return JsonSerializer.SerializeToElement(new { error = exception.ToString() });
            return result.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : JsonSerializer.SerializeToElement("undefined");
        }
        catch (Exception ex) { return JsonSerializer.SerializeToElement(new { error = ex.Message }); }
    }
}
