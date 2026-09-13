using System.Reflection;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

/// <summary>Controlled form/OTP flows, not a claim of real-provider MFA compatibility.</summary>
internal static class AuthenticationFlowScenario
{
    internal static async Task RunAsync()
    {
        await using var app = new LoopbackSite("127.0.0.1");
        await using var provider = new LoopbackSite("127.0.0.2");
        await using var denied = new LoopbackSite("127.0.0.3");
        await using var insecure = new LoopbackSite("127.0.0.4", "mail.zenith.test");
        app.Response = path => path switch
        {
            "/sign-in" => (302, $"Location: {provider.Origin}/login\r\n", ""),
            "/relay307" => (307, $"Location: {denied.Origin}/credential-leak\r\n", ""),
            "/relay308" => (308, $"Location: {insecure.Origin}/credential-leak\r\n", ""),
            _ => (200, "", "<html><body>Application</body></html>")
        };
        provider.DetailedResponse = request => request.Path switch
        {
            "/login" => (200, "", "<html><body><form method='post' action='/password'><input name='password' type='password'><button>Continue</button></form></body></html>"),
            "/password" when request.Method == "POST" && request.Body == "password=fixture-only" =>
                (303, "Location: /mfa\r\n", ""),
            "/mfa" => (200, "", "<html><body><form method='post' action='/verify'><input name='otp'><button>Confirm</button></form></body></html>"),
            "/verify" when request.Method == "POST" && request.Body == "otp=000000" =>
                (303, $"Location: {app.Origin}/callback?code=fixture-only&state=fixture-state\r\n", ""),
            _ => (403, "", "Invalid fixture submission")
        };
        using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
            foreach (var site in new[] { app, denied, insecure })
                Assert.True((await http.GetAsync(site.ListenerOrigin + "/control")).IsSuccessStatusCode);

        var folder = Directory.CreateTempSubdirectory("Zenith-AuthFlowTests-");
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist),
             new SitePolicyEntry("127.0.0.2", AccessClass.Whitelist),
             new SitePolicyEntry("mail.zenith.test", AccessClass.Whitelist)])));
        var window = new MainWindow(new NavigationCoordinator(evaluator))
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new()
        {
            UserDataFolder = folder.FullName,
            AdditionalBrowserArguments = "--host-resolver-rules=\"MAP mail.zenith.test 127.0.0.4\" --no-proxy-server"
        };
        CoreWebView2Environment? environment = null;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            environment = browser.CoreWebView2.Environment;
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            await UntilAsync(() => Task.FromResult((bool)ActiveTab(window).GetType().GetProperty("IsReady")!.GetValue(ActiveTab(window))!));

            // Protection must outlive the original controller, not migrate with the active tab.
            var firstTab = ActiveTab(window);
            await (Task)typeof(MainWindow).GetMethod("OpenShortcutTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [new Uri(app.Origin + "/start"), false])!;
            Assert.Same(firstTab, ActiveTab(window));
            var rows = ((StackPanel)window.FindName("OpenTabsPanel")).Children.OfType<Grid>().ToArray();
            Assert.Equal(2, rows.Length);
            rows[1].Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotSame(firstTab, ActiveTab(window));
            typeof(MainWindow).GetMethod("SetSidebarExpanded", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [false]);
            var firstRow = ((StackPanel)window.FindName("OpenTabsPanel")).Children.OfType<Grid>().First();
            firstRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle)
                { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = firstRow });
            Assert.Single(((StackPanel)window.FindName("OpenTabsPanel")).Children.OfType<Grid>());
            var core = ((WebView2)ActiveTab(window).GetType().GetProperty("Browser")!.GetValue(ActiveTab(window))!).CoreWebView2;
            await PageAsync(core, app.Origin + "/start");

            Request(window, app.Origin + "/sign-in");
            await PageAsync(core, provider.Origin + "/login");
            await core.ExecuteScriptAsync("document.querySelector('input').value='fixture-only'; document.forms[0].requestSubmit();");
            await PageAsync(core, provider.Origin + "/mfa");
            await core.ExecuteScriptAsync("document.querySelector('input').value='000000'; document.forms[0].requestSubmit();");
            await PageAsync(core, app.Origin + "/callback?code=fixture-only&state=fixture-state");
            Assert.Equal(app.Origin + " — Zenith", window.Title);
            Assert.DoesNotContain("fixture-state", window.Title);
            Assert.Contains(provider.Received, r => r.Path == "/password" && r.Body == "password=fixture-only");
            Assert.Contains(provider.Received, r => r.Path == "/verify" && r.Body == "otp=000000");

            foreach (var relay in new[] { "/relay307", "/relay308" })
            {
                Request(window, app.Origin + "/before" + relay);
                await PageAsync(core, app.Origin + "/before" + relay);
                await core.ExecuteScriptAsync("const f=document.createElement('form'); f.method='POST'; f.action=" +
                    JsonSerializer.Serialize(app.Origin + relay) + "; f.innerHTML='<input name=password value=fixture-only>'; document.body.append(f); f.submit();");
                await UntilAsync(() => Task.FromResult(core.Source == "about:blank"));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
                Assert.Contains(app.Received, r => r.Path == relay && r.Method == "POST" && r.Body == "password=fixture-only");
            }
            Assert.DoesNotContain(denied.Requests, p => p == "/credential-leak");
            Assert.DoesNotContain(insecure.Requests, p => p == "/credential-leak");

            Request(window, app.Origin + "/network-probes");
            await PageAsync(core, app.Origin + "/network-probes");
            var rejectedResources = new List<string>();
            core.WebResourceRequested += (_, e) => { if (e.Response?.StatusCode == 403) rejectedResources.Add(e.Request.Uri); };
            await core.ExecuteScriptAsync("window.fetchDone=false; fetch(" + JsonSerializer.Serialize(insecure.Origin + "/insecure-fetch") +
                ", {mode:'no-cors'}).finally(()=>window.fetchDone=true).catch(()=>{});");
            await UntilAsync(() => Task.FromResult(rejectedResources.Contains(insecure.Origin + "/insecure-fetch")));
            Assert.DoesNotContain("/insecure-fetch", insecure.Requests);

            // Dedicated workers are document-scoped: transport still applies. Shared
            // workers remain independently denied after the original tab was closed.
            var dedicatedScript = "onmessage=e=>fetch(e.data,{mode:'no-cors'}).then(()=>postMessage('done')).catch(()=>postMessage('denied'));";
            var sharedScript = "onconnect=e=>{let p=e.ports[0]; p.onmessage=m=>fetch(m.data).then(r=>p.postMessage(r.status)).catch(()=>p.postMessage('failed'));p.start();};";
            await core.ExecuteScriptAsync("window.dedicated=new Worker(URL.createObjectURL(new Blob([" + JsonSerializer.Serialize(dedicatedScript) +
                "],{type:'text/javascript'}))); dedicated.postMessage(" + JsonSerializer.Serialize(insecure.Origin + "/insecure-worker") + ");" +
                "window.shared=new SharedWorker(URL.createObjectURL(new Blob([" + JsonSerializer.Serialize(sharedScript) +
                "],{type:'text/javascript'}))); window.sharedStatus=null; shared.port.onmessage=e=>window.sharedStatus=e.data; shared.port.start(); shared.port.postMessage(location.origin+'/background-after-close');");
            await UntilAsync(() => Task.FromResult(rejectedResources.Contains(insecure.Origin + "/insecure-worker")));
            await UntilAsync(async () => await core.ExecuteScriptAsync("sharedStatus") == "403");
            Assert.DoesNotContain("/insecure-worker", insecure.Requests);
            Assert.DoesNotContain("/background-after-close", app.Requests);
            await core.ExecuteScriptAsync("dedicated.terminate(); shared.port.close();");
        }
        finally
        {
            window.Close();
            if (environment is not null) await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            folder.Delete(true);
        }
    }

    private static object ActiveTab(MainWindow window) => typeof(MainWindow)
        .GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static void Request(MainWindow window, string address) => typeof(MainWindow)
        .GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [address, NavigationOrigin.AddressBar]);

    private static async Task PageAsync(CoreWebView2 core, string address)
    {
        for (var i = 0; i < 500; i++)
        {
            if (core.Source == address && await core.ExecuteScriptAsync("document.readyState") == "\"complete\"") return;
            await Task.Delay(20);
        }
        Assert.Fail($"Expected fixture page {address}, observed {core.Source}.");
    }

    private static async Task UntilAsync(Func<Task<bool>> predicate)
    {
        for (var i = 0; i < 500; i++)
        {
            if (await predicate()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Controlled authentication/transport flow did not reach the expected state.");
    }
}
