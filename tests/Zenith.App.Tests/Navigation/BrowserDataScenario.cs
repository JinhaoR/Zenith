using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.BrowserData;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class BrowserDataScenario
{
    internal static async Task RunAsync()
    {
        await using var site = new LoopbackSite("127.0.0.1");
        foreach (var kind in Enum.GetValues<BrowserDataKind>())
        {
            var folder = Directory.CreateTempSubdirectory("Zenith-BrowserData-");
            try
            {
                // Seed and shut down first, proving this data survives a restart.
                await Session(async (host, browser) =>
                {
                    typeof(MainWindow).GetMethod("OpenSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, ["General"]);
                    var settings = (Window)typeof(MainWindow).GetField("_settingsWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                    var description = ((System.Windows.Controls.TextBlock)settings.FindName("BrowserDataStatusText")).Text;
                    Assert.Contains(browser.CoreWebView2.Environment.UserDataFolder, description);
                    Assert.Contains(browser.CoreWebView2.Profile.ProfilePath, description);
                    var actions = (System.Windows.Controls.WrapPanel)settings.FindName("BrowserDataActions");
                    Assert.True(actions.IsEnabled);
                    Assert.Equal(Enum.GetValues<BrowserDataKind>().Length, actions.Children.Count);
                    settings.Close();
                    await browser.CoreWebView2.ExecuteScriptAsync("document.cookie='data-fixture=persistent; max-age=86400; path=/'; localStorage.setItem('data-fixture','persistent'); caches.open('data-fixture').then(c=>c.put('/cached-fixture',new Response('persistent'))).then(()=>window.seeded=true)");
                    await Until(async () => await browser.CoreWebView2.ExecuteScriptAsync("window.seeded===true") == "true");
                });
                await Session(async (host, browser) =>
                {
                    Assert.NotEmpty(await browser.CoreWebView2.CookieManager.GetCookiesAsync(site.Origin));
                    Assert.Equal("\"persistent\"", await browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('data-fixture')"));
                    await host.ClearBrowsingDataAndCloseAsync(kind);
                    Assert.False(host.IsVisible);
                });
                await Session(async (_, browser) =>
                {
                    var cookiesRemoved = kind is BrowserDataKind.All or BrowserDataKind.Cookies or BrowserDataKind.SiteData;
                    var storageRemoved = kind is BrowserDataKind.All or BrowserDataKind.SiteData;
                    Assert.Equal(cookiesRemoved, (await browser.CoreWebView2.CookieManager.GetCookiesAsync(site.Origin)).Count == 0);
                    Assert.Equal(storageRemoved ? "null" : "\"persistent\"", await browser.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('data-fixture')"));
                    await browser.CoreWebView2.ExecuteScriptAsync("caches.has('data-fixture').then(v=>window.cachePresent=v)");
                    await Until(async () => await browser.CoreWebView2.ExecuteScriptAsync("typeof window.cachePresent==='boolean'") == "true");
                    Assert.Equal(storageRemoved ? "false" : "true", await browser.CoreWebView2.ExecuteScriptAsync("window.cachePresent"));
                });
                Console.WriteLine($"Browser data {kind}: restart persistence, selective cookie/localStorage/CacheStorage results and closed-tab clearing passed");
            }
            finally
            {
                try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            async Task Session(Func<MainWindow, WebView2, Task> action)
            {
                var host = new MainWindow(new NavigationCoordinator(new SitePolicyNavigationEvaluator(
                    new FixedSitePolicySource(new SitePolicySnapshot([new("127.0.0.1", AccessClass.Whitelist)])))))
                    { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                var browser = (WebView2)host.FindName("Browser");
                browser.CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" };
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    host.Show();
                    await browser.EnsureCoreWebView2Async();
                    browser.CoreWebView2.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
                    await Until(() =>
                    {
                        var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                        return Task.FromResult((bool)tab.GetType().GetProperty("IsReady")!.GetValue(tab)!);
                    });
                    var loaded = new TaskCompletionSource();
                    browser.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); };
                    typeof(MainWindow).GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(host, [site.Origin + "/fixture", NavigationOrigin.AddressBar]);
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    await action(host, browser);
                }
                finally { host.Close(); await exited.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            }
        }
    }

    private static async Task Until(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Browser data fixture timed out."); await Task.Delay(20); }
    }
}
