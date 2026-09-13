using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Access;
using Zenith.App.Filtering;
using Zenith.App.Navigation;
using Zenith.Core.Filtering;
using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Navigation;

// Opt-in investigation: records policy failures as evidence. A passing harness is
// not a security certification. Run separately from the other WPF Application test.
public sealed class AccountBoundaryInvestigationTests
{
    private readonly List<object> _evidence = [];
    private void Record(string test, object observed)
    {
        _evidence.Add(new { test, observed });
        Console.WriteLine(test + ": " + JsonSerializer.Serialize(observed));
    }

    [AccountInvestigationFact]
    [Trait("Category", "WebView2Investigation")]
    public async Task InvestigateF02AndSensitiveF06()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application
            {
                Resources = (ResourceDictionary)typeof(WebViewNavigationTests).GetMethod("LoadTheme", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!,
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try { await RunAsync(); finished.SetResult(); }
                catch (Exception e) { Record("harness-error", e.ToString()); finished.SetException(e); }
                finally
                {
                    var path = Environment.GetEnvironmentVariable("ZENITH_ACCOUNT_REPORT");
                    if (path is not null) File.WriteAllText(path, JsonSerializer.Serialize(_evidence, new JsonSerializerOptions { WriteIndented = true }));
                    app.Shutdown();
                }
            });
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromMinutes(5));
    }

    private sealed class Blacklist : IBlacklistSource
    {
        public HostsBlacklist Current { get; } = HostsBlacklist.Parse("0.0.0.0 blocked.zenith.test");
    }

    private async Task RunAsync()
    {
        await using var a = new LoopbackSite("127.0.0.1");
        await using var b = new LoopbackSite("127.0.0.2");
        await using var denied = new LoopbackSite("127.0.0.3");
        await using var mandatory = new LoopbackSite("127.0.0.4", "blocked.zenith.test");
        await using var grey = new LoopbackSite("127.0.0.5");
        foreach (var site in new[] { a, b, denied, mandatory, grey })
            site.Response = path =>
            {
                if (path.StartsWith("/redirect/")) return (int.Parse(path.Split('/')[2]), "Location: " + denied.Origin + "/credential-sink\r\n", "");
                return (200, "Cache-Control: no-store\r\n", "<html><body>synthetic-account-" + site.Origin +
                    "<script>window.secret='dom-secret';window.reports=[];onmessage=e=>reports.push({origin:e.origin,data:e.data});parent.postMessage({loaded:location.href},'*');</script></body></html>");
            };
        var folder = Directory.CreateTempSubdirectory("Zenith-AccountBoundary-");
        var syntheticFile = Path.Combine(folder.FullName, "synthetic-private.txt");
        File.WriteAllText(syntheticFile, "synthetic-file-secret");
        using var vault = new ProtectedAccessStore(Path.Combine(folder.FullName, "Vault"));
        vault.Initialize("synthetic investigation password", DateTimeOffset.UtcNow);
        var vaultPath = Path.Combine(folder.FullName, "Vault", "access.bin");
        var vaultBefore = File.ReadAllBytes(vaultPath);
        var blacklist = new Blacklist();
        var policy = new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist), new SitePolicyEntry("127.0.0.2", AccessClass.Whitelist),
             new SitePolicyEntry("127.0.0.3", AccessClass.Blacklist)], mandatoryBlacklist: blacklist.Current));
        var coordinator = new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy));
        using var http = new HttpClient(new AdblockWebViewFixture());
        using var adblock = new AdblockService(Path.Combine(folder.FullName, "Filters"), http);
        await adblock.UpdateAsync();
        var vaultService = new VaultService(vault, TimeProvider.System, blacklist);
        var profile = Path.Combine(folder.FullName, "Profile");
        await SeedHandle(profile, a.Origin, syntheticFile);
        var window = new MainWindow(coordinator, vaultService: vaultService, blacklist: blacklist, adblock: adblock)
            { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new() { UserDataFolder = profile, AdditionalBrowserArguments = TestArguments };
        CoreWebView2Environment? environment = null;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            environment = core.Environment;
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            await Until(() => Task.FromResult((bool)Tab(window).GetType().GetProperty("IsReady")!.GetValue(Tab(window))!));
            Record("candidate", new { runtime = environment.BrowserVersionString, sdk = typeof(CoreWebView2).Assembly.GetName().Version!.ToString(), flags = browser.CreationProperties.AdditionalBrowserArguments, webMessages = core.Settings.IsWebMessageEnabled, hostObjects = core.Settings.AreHostObjectsAllowed });
            Record("legacy-file-permission-reset", new { anyAllowed = (await core.Profile.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == CoreWebView2PermissionKind.FileReadWrite && p.PermissionState == CoreWebView2PermissionState.Allow) });
            core.PermissionRequested += (_, e) => Record("permission", new { kind = e.PermissionKind.ToString(), e.Uri, state = e.State.ToString(), e.Handled, e.SavesInProfile, e.IsUserInitiated });
            var fetchObserved = new List<string>();
            var nativeObserved = new List<string>();
            core.GetDevToolsProtocolEventReceiver("Fetch.requestPaused").DevToolsProtocolEventReceived += (_, e) =>
            {
                using var json = JsonDocument.Parse(e.ParameterObjectAsJson);
                fetchObserved.Add(json.RootElement.GetProperty("request").GetProperty("url").GetString()!);
            };
            void ObserveFrame(object? sender, CoreWebView2FrameCreatedEventArgs e)
            {
                e.Frame.NavigationStarting += (_, navigation) => nativeObserved.Add(navigation.Uri);
                e.Frame.FrameCreated += ObserveFrame;
            }
            core.FrameCreated += ObserveFrame;

            foreach (var target in new[] { denied.Origin, mandatory.Origin, grey.Origin })
            {
                await Open(window, core, a.Origin + "/top");
                Request(window, target + "/address");
                await Task.Delay(250);
                Record("address-denial", new { target, decision = coordinator.EvaluateAddressBarRequest(target).GetType().Name, source = core.Source });
                await Open(window, core, a.Origin + "/script");
                await Eval(core, "location.href=" + J(target + "/script-nav"));
                await Task.Delay(400);
                Record("script-denial", new { target, source = core.Source });
                await Open(window, core, a.Origin + "/popup");
                var tabs = Tabs(window);
                await Eval(core, "window.open(" + J(target + "/popup") + ", '_blank')", gesture: true);
                await Task.Delay(400);
                Record("popup-denial", new { target, before = tabs, after = Tabs(window), source = core.Source });
            }
            foreach (var status in new[] { 302, 303, 307, 308 })
            {
                await Open(window, core, a.Origin + "/form" + status);
                await Eval(core, "const f=document.createElement('form');f.method='POST';f.action='/redirect/" + status + "'; f.innerHTML='<input name=password value=synthetic-password><input name=token value=synthetic-token>';document.body.append(f);f.submit();");
                await Task.Delay(450);
                Record("credential-redirect", new { status, source = core.Source, submitted = a.Received.Any(r => r.Path == "/redirect/" + status && r.Body.Contains("synthetic-password")), leaked = denied.Received.Any(r => r.Path == "/credential-sink") });
            }

            await Open(window, core, a.Origin + "/frames");
            Record("persisted-handle-reopen", await Eval(core, """
                (async()=>{let db=await new Promise((ok,no)=>{let r=indexedDB.open('handles',1);r.onsuccess=()=>ok(r.result);r.onerror=()=>no(r.error)});window.persisted=await new Promise((ok,no)=>{let r=db.transaction('handles').objectStore('handles').get('file');r.onsuccess=()=>ok(r.result);r.onerror=()=>no(r.error)});db.close();let result={exists:!!persisted,permission:await persisted.queryPermission({mode:'read'})};try{result.read=await(await persisted.getFile()).text()}catch(e){result.read=e.name}return result})()
                """, awaitPromise: true));
            Record("persisted-handle-request", await Eval(core, "persisted.requestPermission({mode:'read'}).then(v=>v).catch(e=>e.name)", gesture: true, awaitPromise: true));
            await Eval(core, "localStorage.setItem('secret','storage-A');sessionStorage.setItem('secret','session-A');document.cookie='readable=secret-A; path=/';");
            var cookie = core.CookieManager.CreateCookie("httpOnly", "httpOnly-A", "127.0.0.1", "/"); cookie.IsHttpOnly = true; core.CookieManager.AddOrUpdateCookie(cookie);
            await Eval(core, "window.child=document.createElement('iframe');child.allow='clipboard-read *; display-capture *; camera *; microphone *';child.src=" + J(b.Origin + "/child") + ";document.body.append(child)");
            await Until(async () => (await Eval(core, "reports.some(r=>r.data.loaded===" + J(b.Origin + "/child") + ")")).GetBoolean());
            var processes = await environment.GetProcessExtendedInfosAsync();
            var frames = processes.SelectMany(p => p.AssociatedFrameInfos.Select(f => new { pid = p.ProcessInfo.ProcessId, f.Source, f.FrameId, parent = f.ParentFrameInfo?.FrameId })).ToArray();
            Record("frame-processes", frames);
            Assert.NotEqual(frames.Single(f => f.Source == a.Origin + "/frames").pid, frames.Single(f => f.Source == b.Origin + "/child").pid);
            var targets = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargets", "{}")).RootElement.GetProperty("targetInfos");
            var targetId = targets.EnumerateArray().Single(t => t.GetProperty("type").GetString() == "iframe" && t.GetProperty("url").GetString() == b.Origin + "/child").GetProperty("targetId").GetString();
            var session = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget", J(new { targetId, flatten = true }))).RootElement.GetProperty("sessionId").GetString()!;

            Record("origin-isolation-child", await Eval(core, """
                (()=>{const r={};for(const [k,f] of Object.entries({dom:()=>parent.document.body.textContent,cookie:()=>parent.document.cookie,local:()=>parent.localStorage.secret,session:()=>parent.sessionStorage.secret})){try{r[k]=f()}catch(e){r[k]=e.name}}r.ownCookie=document.cookie;r.ownLocal=localStorage.secret||null;r.ownSession=sessionStorage.secret||null;return r})()
                """, session));
            Record("origin-isolation-parent", await Eval(core, "(()=>{try{return child.contentWindow.document.body.textContent}catch(e){return e.name}})()"));
            Record("httpOnly-hidden", await Eval(core, "!document.cookie.includes('httpOnly-A')"));
            Record("origin-idb-child", await Eval(core, "indexedDB.databases().then(d=>d.map(x=>x.name))", session, awaitPromise: true));
            await Eval(core, "window.transferResult='pending';addEventListener('messageerror',()=>transferResult='messageerror');addEventListener('message',async e=>{if(e.data?.handle){try{transferResult=await(await e.data.handle.getFile()).text()}catch(err){transferResult=err.name}}})", session);
            Record("cross-origin-handle-send", await Eval(core, "(()=>{try{child.contentWindow.postMessage({handle:persisted},'*');return 'sent'}catch(e){return e.name}})()"));
            await Task.Delay(250);
            Record("cross-origin-handle-receive", await Eval(core, "transferResult", session));
            Record("cross-origin-fetch", await Eval(core, "fetch(" + J(a.Origin + "/authenticated-content") + ", {credentials:'include'}).then(r=>r.text()).then(v=>'READ:'+v).catch(e=>e.name)", session, awaitPromise: true));

            foreach (var host in new[] { denied, mandatory, grey })
            {
                foreach (var context in new[] { "root", "oopif" })
                {
                    var url = host.Origin + "/nested-" + context;
                    await Eval(core, "(()=>{const f=document.createElement('iframe');f.src=" + J(url) + ";document.body.append(f)})()", context == "oopif" ? session : null);
                    await Task.Delay(1500);
                    Record("frame-document", new { context, url, decision = coordinator.EvaluateDocumentRequest(url, false, a.Origin).GetType().Name, reachedServer = host.Requests.Contains("/nested-" + context), executed = await Eval(core, "reports.some(r=>r.origin===" + J(host.Origin) + "&&r.data.loaded===" + J(url) + ")", context == "oopif" ? session : null), frames = (await environment.GetProcessExtendedInfosAsync()).SelectMany(p => p.AssociatedFrameInfos).Select(f => f.Source).Where(s => s.Contains("nested-")).ToArray() });
                }
            }
            foreach (var status in new[] { 302, 303, 307, 308 })
            {
                var before = denied.Received.Count;
                await Eval(core, "(()=>{const frame=document.createElement('iframe');frame.name='auth" + status + "';document.body.append(frame);const f=document.createElement('form');f.target=frame.name;f.method='POST';f.action='/redirect/" + status + "';f.innerHTML='<input name=password value=synthetic-oopif-password><input name=token value=synthetic-oopif-token>';document.body.append(f);f.submit()})()", session);
                await Task.Delay(1200);
                Record("oopif-credential-redirect", new { status, submitted = b.Received.Any(r => r.Path == "/redirect/" + status && r.Body.Contains("synthetic-oopif-password")), receivedByDenied = denied.Received.Skip(before).ToArray() });
            }
            var previousClipboard = Clipboard.GetDataObject();
            Clipboard.SetText("zenith-synthetic-clipboard");
            window.Activate();
            browser.Focus();
            try
            {
            foreach (var context in new[] { "root", "oopif" })
            {
                var sid = context == "oopif" ? session : null;
                if (sid is not null) await Eval(core, "child.focus();child.contentWindow.focus()");
                else browser.Focus();
                Record("focus-" + context, await Eval(core, "document.hasFocus()", sid));
                Record("bridge-attack-" + context, await Eval(core, """
                    (()=>{let sent=0;for(const kind of ['vault.unlock','policy.change','readFile','writeFile','execute','download','zenith-cosmetics']){try{chrome.webview.postMessage({kind,path:'C:/synthetic-private.txt',password:'synthetic',url:location.href,token:'probe',classes:[],ids:[],hrefs:[]});sent++}catch{}}return {sent,hostObjects:typeof chrome.webview.hostObjects}})()
                    """, sid));
                Record("host-object-call-" + context, await Eval(core, "Promise.resolve().then(()=>chrome.webview.hostObjects.vault.unlock('synthetic')).then(()=> 'invoked').catch(e=>e.name+':'+e.message)", sid, awaitPromise: true));
                await Task.Delay(500);
                await Eval(core, "window.nativeReplies=[];chrome.webview.addEventListener('message',e=>nativeReplies.push(e.data));chrome.webview.postMessage({kind:'zenith-cosmetics',token:'positive-control',url:location.href,classes:[],ids:[],hrefs:[],action:'vault.unlock',path:'C:/synthetic-private.txt'})", sid);
                await Until(async () => (await Eval(core, "nativeReplies.some(r=>r.token==='positive-control')", sid)).GetBoolean());
                Record("bridge-reply-" + context, await Eval(core, "nativeReplies.filter(r=>r.token==='positive-control').map(r=>({kind:r.kind,url:r.url,fields:Object.keys(r)}))", sid));
                await CapabilityProbes(core, sid, context);
            }
            }
            finally
            {
                if (Clipboard.GetText() == "zenith-synthetic-clipboard")
                {
                    if (previousClipboard is not null) Clipboard.SetDataObject(previousClipboard, true);
                    else Clipboard.Clear();
                }
            }
            Record("bridge-state", new { vaultUnchanged = vaultBefore.SequenceEqual(File.ReadAllBytes(vaultPath)), deniedDecision = coordinator.EvaluateAddressBarRequest(denied.Origin).GetType().Name, fileUnchanged = File.ReadAllText(syntheticFile) == "synthetic-file-secret" });
            var beforePopup = Tabs(window);
            await Eval(core, "window.open(" + J(denied.Origin + "/oopif-popup") + ",'_blank')", session, gesture: true);
            await Task.Delay(500);
            Record("oopif-popup", new { before = beforePopup, after = Tabs(window), source = core.Source, reached = denied.Requests.Contains("/oopif-popup") });
            Record("denied-server-requests", new { vault = denied.Received.ToArray(), mandatory = mandatory.Received.ToArray(), grey = grey.Received.ToArray() });
            Record("interception-observations", new
            {
                rootFetchDeniedChild = fetchObserved.Contains(denied.Origin + "/nested-root"),
                rootFetchOopifGrandchild = fetchObserved.Contains(denied.Origin + "/nested-oopif"),
                nativeFrameDeniedChild = nativeObserved.Contains(denied.Origin + "/nested-root"),
                nativeFrameOopifGrandchild = nativeObserved.Contains(denied.Origin + "/nested-oopif"),
                nativeCredentialSink = nativeObserved.Contains(denied.Origin + "/credential-sink")
            });
        }
        finally
        {
            window.Close();
            if (environment is not null) { try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (TimeoutException) { } }
            vault.Dispose();
            try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private async Task SeedHandle(string profile, string origin, string file)
    {
        using var browser = new WebView2 { CreationProperties = new() { UserDataFolder = profile, AdditionalBrowserArguments = TestArguments } };
        var host = new Window { Content = browser, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            core.Navigate(origin + "/seed-handle");
            await Until(async () => core.Source == origin + "/seed-handle" && (await Eval(core, "document.readyState")).GetString() == "complete");
            await Eval(core, """
                chrome.webview.addEventListener('message',async e=>{window.handle=e.additionalObjects[0];window.seedRead=await(await handle.getFile()).text();const db=await new Promise((ok,no)=>{let r=indexedDB.open('handles',1);r.onupgradeneeded=()=>r.result.createObjectStore('handles');r.onsuccess=()=>ok(r.result);r.onerror=()=>no(r.error)});await new Promise((ok,no)=>{let t=db.transaction('handles','readwrite');t.objectStore('handles').put(handle,'file');t.oncomplete=ok;t.onerror=()=>no(t.error)});db.close();window.seeded=true;});
                """);
            // Test-only explicit native file grant, never a production bridge. Verify
            // the positive control before testing same-profile restart restrictions.
            var handle = core.Environment.CreateWebFileSystemFileHandle(file, CoreWebView2FileSystemHandlePermission.ReadOnly);
            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.FileReadWrite, origin, CoreWebView2PermissionState.Allow);
            Record("legacy-file-permission-seed", new { anyAllowed = (await core.Profile.GetNonDefaultPermissionSettingsAsync()).Any(p => p.PermissionKind == CoreWebView2PermissionKind.FileReadWrite && p.PermissionState == CoreWebView2PermissionState.Allow) });
            core.PostWebMessageAsJson("{}", [handle]);
            await Until(async () => (await Eval(core, "window.seeded===true")).GetBoolean());
            Record("persisted-handle-seed", await Eval(core, "({read:seedRead,stored:seeded})"));
        }
        finally
        {
            browser.Dispose();
            host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static string TestArguments => "--no-proxy-server --host-resolver-rules=\"MAP blocked.zenith.test 127.0.0.4\"" +
        (Environment.GetEnvironmentVariable("ZENITH_FAKE_MEDIA") == "1" ? " --use-fake-device-for-media-stream" : "");

    private async Task CapabilityProbes(CoreWebView2 core, string? session, string context)
    {
        foreach (var gesture in new[] { false, true })
        {
            foreach (var api in new[] { "showOpenFilePicker()", "showDirectoryPicker()", "showSaveFilePicker()", "navigator.clipboard.readText()", "navigator.clipboard.read()", "navigator.mediaDevices.getUserMedia({video:true,audio:true})" })
            {
                await Eval(core, "window.probe='pending';try{Promise.resolve(" + api + ").then(v=>{window.probe='resolved';if(v?.getTracks)v.getTracks().forEach(t=>t.stop())}).catch(e=>window.probe=e.name+':'+e.message)}catch(e){window.probe=e.name+':'+e.message}", session, gesture);
                await Task.Delay(350);
                var dialogs = CloseFixtureDialogs(core);
                Record("capability", new { context, api, gesture, result = await Eval(core, "window.probe", session), dialogs });
            }
            await Eval(core, "window.input=document.createElement('input');input.type='file';document.body.append(input);window.cancelled=false;input.oncancel=()=>cancelled=true;input.click()", session, gesture);
            await Task.Delay(350);
            Record("html-picker", new { context, gesture, dialogs = CloseFixtureDialogs(core), result = await Eval(core, "({files:input.files.length,cancelled})", session) });
        }
        // Observe production decision, then cancel in the test to avoid capturing a
        // real desktop. This is evidence of the decision, not denial by Zenith.
        void Capture(object? sender, CoreWebView2ScreenCaptureStartingEventArgs e)
        {
            Record("screen-capture-native", new { context, productionCancel = e.Cancel, e.Handled, source = e.OriginalSourceFrameInfo.Source });
            e.Cancel = true;
        }
        core.ScreenCaptureStarting += Capture;
        try
        {
            Record("screen-capture", await Eval(core, "navigator.mediaDevices.getDisplayMedia({video:true}).then(s=>{s.getTracks().forEach(t=>t.stop());return 'resolved'}).catch(e=>e.name)", session, gesture: true, awaitPromise: true));
        }
        finally { core.ScreenCaptureStarting -= Capture; }
    }

    private static async Task<JsonElement> Eval(CoreWebView2 core, string expression, string? session = null, bool gesture = false, bool awaitPromise = false)
    {
        var parameters = J(new { expression, userGesture = gesture, awaitPromise, returnByValue = true });
        var json = session is null ? await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters).WaitAsync(TimeSpan.FromSeconds(8))
            : await core.CallDevToolsProtocolMethodForSessionAsync(session, "Runtime.evaluate", parameters).WaitAsync(TimeSpan.FromSeconds(8));
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.ToString());
        return doc.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : JsonSerializer.SerializeToElement("undefined");
    }
    private static string J(object value) => JsonSerializer.Serialize(value);
    private static object Tab(MainWindow window) => typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static int Tabs(MainWindow window) => ((System.Collections.ICollection)typeof(MainWindow).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Count;
    private static void Request(MainWindow window, string target) => typeof(MainWindow).GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [target, NavigationOrigin.AddressBar]);
    private static async Task Open(MainWindow window, CoreWebView2 core, string target)
    {
        Request(window, target);
        await Until(async () => core.Source == target && (await Eval(core, "document.readyState")).GetString() == "complete");
    }
    private static async Task Until(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 400; i++) { if (await condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Account fixture state did not arrive.");
    }

    private static List<string> CloseFixtureDialogs(CoreWebView2 core)
    {
        var pids = core.Environment.GetProcessInfos().Select(p => p.ProcessId).Append(Environment.ProcessId).ToHashSet();
        var found = new List<string>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains((int)pid) || !IsWindowVisible(hwnd)) return true;
            var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() != "#32770") return true;
            found.Add("native-file-dialog");
            PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return found;
    }
    private delegate bool EnumWindow(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}

public sealed class AccountInvestigationFactAttribute : FactAttribute
{
    public AccountInvestigationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_ACCOUNT_INVESTIGATION") != "1")
            Skip = "Run this evidence-collecting WPF investigation separately with ZENITH_ACCOUNT_INVESTIGATION=1; it records known policy failures.";
    }
}
