using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Extensions;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class BundledExtensionScenario
{
    internal static async Task RunAsync()
    {
        await using var site = new LoopbackSite("127.0.0.1");
        await using var denied = new LoopbackSite("127.0.0.2");
        site.Response = path => path == "/redirect"
            ? (307, $"Location: {denied.Origin}/denied\r\n", "")
            : (200, "", "<html><body><h1>Allowed page</h1><input type=password></body></html>");
        var folder = Directory.CreateTempSubdirectory("Zenith-BundledExtension-");
        string? id = null;
        try
        {
            // Real MainWindow startup twice against the same disposable browser profile.
            for (var session = 0; session < 2; session++)
            {
                var policy = new FixedSitePolicySource(new SitePolicySnapshot([
                    new("127.0.0.1", AccessClass.Whitelist), new("127.0.0.2", AccessClass.Blacklist)]));
                var host = new MainWindow(new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy)))
                    { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
                var browser = (WebView2)host.FindName("Browser");
                browser.CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" };
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var initialized = false;
                try
                {
                    host.Show();
                    await browser.EnsureCoreWebView2Async();
                    var core = browser.CoreWebView2;
                    core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
                    initialized = true;
                    await Until(() => Task.FromResult((bool)typeof(MainWindow).GetField("_builtInExtensionsReady", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!));
                    var extension = Assert.Single(await core.Profile.GetBrowserExtensionsAsync(), x => x.Name == "uBlock Origin Lite");
                    Assert.True(extension.IsEnabled);
                    if (id is not null) Assert.Equal(id, extension.Id);
                    id = extension.Id;
                    Assert.Equal(ContentProtectionStatus.Enabled, await ContentProtectionReader.ReadAsync(core.Profile));
                    await Navigate(site.Origin + "/page", true);
                    Assert.Equal("\"Allowed page\"", await core.ExecuteScriptAsync("document.querySelector('h1').textContent"));
                    await Probe("enabled");
                    Assert.DoesNotContain("/xpopup/xpopup.js?enabled", site.Requests);
                    Assert.DoesNotContain("/analytics/rakuten.js?enabled", site.Requests);
                    Assert.Contains("/ordinary-resource?enabled", site.Requests);

                    // Negative control proves the absent requests were extension filtering,
                    // not a browser error or a matching rule in Zenith's other filter engine.
                    await extension.EnableAsync(false);
                    await Probe("disabled");
                    Assert.Contains("/xpopup/xpopup.js?disabled", site.Requests);
                    Assert.Contains("/analytics/rakuten.js?disabled", site.Requests);
                    await extension.EnableAsync(true);

                    await Navigate(denied.Origin + "/direct", false);
                    await Navigate(site.Origin + "/redirect", false);
                    await Navigate($"chrome-extension://{id}/dashboard.html", false);
                    Assert.Empty(denied.Requests);
                    Console.WriteLine($"Bundled uBO Lite {BundledExtensions.UbolVersion}, WebView2 {core.Environment.BrowserVersionString}, session {session}: enabled, stable identity, allowed DOM, DNR block/disable controls, denied direct/307/dashboard passed; denied server requests=0");

                    async Task Probe(string mode)
                    {
                        await core.ExecuteScriptAsync($$"""
                            window.probeDone=false;
                            Promise.all(['/xpopup/xpopup.js','/analytics/rakuten.js','/ordinary-resource'].map(p=>fetch(p+'?{{mode}}').catch(()=>null))).then(()=>window.probeDone=true)
                            """);
                        await Until(async () => await core.ExecuteScriptAsync("window.probeDone") == "true");
                    }

                    async Task Navigate(string target, bool success)
                    {
                        var completed = new TaskCompletionSource<bool>();
                        core.NavigationCompleted += Completed;
                        try
                        {
                            core.Navigate(target);
                            Assert.Equal(success, await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                            if (!success)
                            {
                                await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                                await Until(() =>
                                {
                                    var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
                                    var clearance = (DocumentClearance?)tab.GetType().GetProperty("Clearance")!.GetValue(tab);
                                    return Task.FromResult(core.Source == "about:blank" && clearance?.IsPending != true);
                                });
                            }
                        }
                        finally { core.NavigationCompleted -= Completed; }
                        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                        {
                            if (core.Source != "about:blank" || !e.IsSuccess) completed.TrySetResult(e.IsSuccess);
                        }
                    }
                }
                catch (Exception error)
                {
                    Console.WriteLine(error);
                    throw;
                }
                finally
                {
                    host.Close();
                    if (initialized) await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
        }
        finally { try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static async Task Until(Func<Task<bool>> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await ready())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Bundled extension scenario did not complete.");
            await Task.Delay(25);
        }
    }
}
