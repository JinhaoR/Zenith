using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.Core.Filtering;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class NetworkEnforcementScenario
{
    private sealed class Blacklist : IBlacklistSource
    {
        public HostsBlacklist Current { get; set; } = HostsBlacklist.Parse("0.0.0.0 blocked.zenith.test");
    }

    internal static async Task RunAsync()
    {
        await using var allowed = new LoopbackSite("127.0.0.1");
        await using var grey = new LoopbackSite("127.0.0.2");
        await using var black = new LoopbackSite("127.0.0.3", "blocked.zenith.test");
        // Positive controls prove all three observers can receive real requests.
        using (var http = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { UseProxy = false }))
        {
            foreach (var site in new[] { allowed, grey, black })
                Assert.True((await http.GetAsync(site.ListenerOrigin + "/control")).IsSuccessStatusCode);
        }
        allowed.Response = path => path switch
        {
            "/redirect" => (302, $"Location: {grey.Origin}/denied-redirect\r\n", ""),
            "/hop" => (302, $"Location: {allowed.Origin}/redirect\r\n", ""),
            "/allowed-redirect" => (302, $"Location: {allowed.Origin}/landing\r\n", ""),
            _ => (200, "", "<html><body>Allowed test page</body></html>")
        };
        grey.Response = path => path == "/permitted-widget"
            ? (200, "", $"<html><body>Widget<iframe src='{grey.Origin}/nested-widget'></iframe><iframe src='{black.Origin}/denied-nested'></iframe></body></html>")
            : (200, "", "<html><body>Nested widget</body></html>");
        var profile = Directory.CreateTempSubdirectory("Zenith-NetworkTests-");
        var blacklist = new Blacklist();
        var policy = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist)], mandatoryBlacklist: blacklist.Current)));
        var window = new MainWindow(new NavigationCoordinator(policy), blacklist: blacklist)
        { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = profile.FullName,
            AdditionalBrowserArguments = "--host-resolver-rules=\"MAP blocked.zenith.test 127.0.0.3\" --no-proxy-server"
        };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreWebView2Environment? environment = null;
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            environment = browser.CoreWebView2.Environment;
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            await WaitAsync(() =>
            {
                var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                return (bool)tab.GetType().GetProperty("IsReady")!.GetValue(tab)!;
            });
            var core = browser.CoreWebView2;
            async Task Open(string path)
            {
                Request(window, allowed.Origin + path);
                await WaitAsync(() => core.Source == allowed.Origin + path);
                for (var i = 0; i < 100; i++)
                {
                    if (await core.ExecuteScriptAsync("document.readyState") == "\"complete\"") return;
                    await Task.Delay(20);
                }
                Assert.Fail("Loopback page did not load.");
            }
            await Open("/start");
            Request(window, grey.Origin + "/denied-direct");
            await WaitAsync(() => core.Source == "about:blank");
            await Open("/start-again");
            var mandatory = blacklist.Current;
            blacklist.Current = HostsBlacklist.Parse("0.0.0.0 unrelated.zenith.test");
            await core.ExecuteScriptAsync("fetch(" + JsonSerializer.Serialize(black.Origin + "/browser-control") + ", {mode:'no-cors'}).catch(() => {})");
            await WaitAsync(() => black.Requests.Contains("/browser-control"));
            blacklist.Current = mandatory;
            Request(window, allowed.Origin + "/allowed-redirect");
            await WaitAsync(() => core.Source == allowed.Origin + "/landing");
            foreach (var path in new[] { "/redirect", "/hop" })
            {
                await Open("/before" + path);
                Request(window, allowed.Origin + path);
                await WaitAsync(() => ((FrameworkElement)window.FindName("BoundarySurface")).Visibility == Visibility.Visible);
                await WaitAsync(() => core.Source == "about:blank");
            }
            await Open("/script");
            await core.ExecuteScriptAsync("location.href = " + JsonSerializer.Serialize(grey.Origin + "/denied-script"));
            await WaitAsync(() => core.Source == "about:blank");
            await Open("/form");
            await core.ExecuteScriptAsync("const form = document.createElement('form'); form.method = 'POST'; form.action = " +
                JsonSerializer.Serialize(grey.Origin + "/denied-form") + "; document.body.appendChild(form); form.submit();");
            await WaitAsync(() => core.Source == "about:blank");
            await Open("/link");
            await core.ExecuteScriptAsync("const link = document.createElement('a'); link.href = " +
                JsonSerializer.Serialize(grey.Origin + "/denied-link") + "; document.body.appendChild(link); link.click();");
            await WaitAsync(() => core.Source == "about:blank");
            await Open("/popup");
            await core.ExecuteScriptAsync("window.open(" + JsonSerializer.Serialize(grey.Origin + "/denied-popup") + ", '_blank')");
            await Task.Delay(300);
            await Open("/embedded");
            await core.ExecuteScriptAsync("const f = document.createElement('iframe'); f.src = " + JsonSerializer.Serialize(grey.Origin + "/permitted-widget") + "; document.body.appendChild(f);");
            await WaitAsync(() => grey.Requests.Contains("/permitted-widget"));
            await WaitAsync(() => grey.Requests.Contains("/nested-widget"));
            await core.ExecuteScriptAsync("const b = document.createElement('iframe'); b.src = " + JsonSerializer.Serialize(black.Origin + "/denied-frame") + "; document.body.appendChild(b);");
            await Task.Delay(500);
            var creation = (Task)typeof(MainWindow).GetMethod("CreateTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(window, [new Uri(allowed.Origin + "/new-tab")])!;
            await creation;
            var newBrowser = ((Panel)window.FindName("BrowserHost")).Children.OfType<WebView2>().Last();
            await WaitAsync(() => allowed.Requests.Contains("/new-tab"));
            Request(window, allowed.Origin + "/redirect");
            await WaitAsync(() => ((FrameworkElement)window.FindName("BoundarySurface")).Visibility == Visibility.Visible);
            await WaitAsync(() => newBrowser.CoreWebView2.Source == "about:blank");
            await Task.Delay(300);
            Assert.Contains("/landing", allowed.Requests);
            Assert.DoesNotContain(grey.Requests, path => path.StartsWith("/denied-", StringComparison.Ordinal));
            Assert.DoesNotContain("/denied-frame", black.Requests);
            Assert.DoesNotContain("/denied-nested", black.Requests);
            Console.WriteLine("Loopback network enforcement: denied HTTP requests absent; permitted redirect and widget observed.");

            // Simulate a native interception-channel failure: live controllers must not remain available.
            var active = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var guard = active.GetType().GetProperty("DocumentGuard")!.GetValue(active)!;
            guard.GetType().GetMethod("FailClosed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(guard, null);
            await WaitAsync(() => !window.IsVisible);
        }
        finally
        {
            window.Close();
            if (environment is not null) await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            profile.Delete(true);
        }
    }

    private static void Request(MainWindow window, string target) =>
        typeof(MainWindow).GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [target, NavigationOrigin.AddressBar]);

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500; i++)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Network test did not reach its expected browser state.");
    }
}
