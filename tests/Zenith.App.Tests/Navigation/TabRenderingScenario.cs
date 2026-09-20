using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class TabRenderingScenario
{
    internal static async Task RunAsync()
    {
        await using var site = new LoopbackSite("127.0.0.1");
        await using var identity = new LoopbackSite("127.0.0.2");
        await using var blocked = new LoopbackSite("127.0.0.3");
        const string page = "<html><body style='background:white;color:black'><h1>Account fixture</h1><form><input type=password></form></body></html>";
        site.Response = path => path switch
        {
            "/login" => (302, $"Location: {identity.Origin}/signin\r\n", ""),
            "/blocked" => (302, $"Location: {blocked.Origin}/denied\r\n", ""),
            _ => (200, "", page)
        };
        identity.Response = _ => (200, "", page);
        var policy = new Policy();
        var folder = Directory.CreateTempSubdirectory("Zenith-TabRendering-");
        var host = new MainWindow(new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy)))
            { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        var original = (WebView2)host.FindName("Browser");
        original.CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            host.Show();
            await original.EnsureCoreWebView2Async();
            original.CoreWebView2.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var originalTab = Field(host, "_activeTab")!;
            await Until(() => (bool)originalTab.GetType().GetProperty("IsReady")!.GetValue(originalTab)!);

            var (browser, tab) = await OpenBackground(site.Origin + "/login");
            Assert.Empty(identity.Requests);
            Assert.Same(originalTab, Field(host, "_activeTab"));
            Invoke(host, "ActivateTab", tab);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)host.FindName("BoundarySurface")).Visibility);
            Assert.Equal(Visibility.Collapsed, browser.Visibility);
            Assert.Equal(identity.Origin + "/signin", ((TextBlock)host.FindName("BoundaryTargetTextBlock")).Text);
            await Until(() => browser.CoreWebView2.Source == "about:blank");

            // A later independent Core authorization permits a fresh attempt,
            // including the exact original URL whose WPF Source was cached on failure.
            policy.AllowIdentity = true;
            await OpenAndVerify(browser, site.Origin + "/login", identity.Origin + "/signin");
            Assert.Equal(2, site.Requests.Count(path => path == "/login"));
            Assert.Contains("/signin", identity.Requests);

            Invoke(host, "ActivateTab", originalTab);
            var (cancelled, cancelledTab) = await OpenBackground(site.Origin + "/cancelled", cancel: true);
            Invoke(host, "ActivateTab", cancelledTab);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)host.FindName("BoundarySurface")).Visibility);
            Assert.Equal(Visibility.Collapsed, cancelled.Visibility);
            Assert.Equal(site.Origin + "/cancelled", ((TextBlock)host.FindName("BoundaryTargetTextBlock")).Text);
            await OpenAndVerify(cancelled, site.Origin + "/cancelled", site.Origin + "/cancelled");

            Invoke(host, "ActivateTab", originalTab);
            var (denied, deniedTab) = await OpenBackground(site.Origin + "/blocked");
            Invoke(host, "ActivateTab", deniedTab);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)host.FindName("BoundarySurface")).Visibility);
            Assert.Contains("Blacklisted", ((TextBlock)host.FindName("BoundaryExplanationTextBlock")).Text);
            Assert.Empty(blocked.Requests);
            Assert.Equal(Visibility.Collapsed, denied.Visibility);

            // Successfully loaded background tabs still resume and render normally.
            Invoke(host, "ActivateTab", originalTab);
            var (allowed, allowedTab) = await OpenBackground(site.Origin + "/allowed", success: true);
            Invoke(host, "ActivateTab", allowedTab);
            await VerifyRendering(allowed);
            Invoke(host, "ActivateTab", tab);
            await VerifyRendering(browser);
            Console.WriteLine("Tab rendering: background denied login/Blacklist redirects and cancelled loads show native failures; exact-URL retry, allowed login and retained/background rendering passed");

            async Task<(WebView2, object)> OpenBackground(string target, bool cancel = false, bool success = false)
            {
                var opening = (Task)Invoke(host, "CreateTabCoreAsync", new Uri(target), false)!;
                var next = ((Grid)host.FindName("BrowserHost")).Children.OfType<WebView2>().Last();
                await next.EnsureCoreWebView2Async();
                var finished = new TaskCompletionSource<bool>();
                next.CoreWebView2.NavigationStarting += CancelOnce;
                next.CoreWebView2.NavigationCompleted += Completed;
                try
                {
                    await opening;
                    Assert.Equal(success, await finished.Task.WaitAsync(TimeSpan.FromSeconds(15)));
                }
                finally
                {
                    next.CoreWebView2.NavigationStarting -= CancelOnce;
                    next.CoreWebView2.NavigationCompleted -= Completed;
                }
                var nextTab = ((System.Collections.IEnumerable)Field(host, "_tabs")!).Cast<object>().Last();
                return (next, nextTab);
                void CancelOnce(object? sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (cancel && e.Uri == target) { cancel = false; e.Cancel = true; }
                }
                void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (next.CoreWebView2.Source != "about:blank" || !e.IsSuccess) finished.TrySetResult(e.IsSuccess);
                }
            }

            async Task OpenAndVerify(WebView2 view, string target, string destination)
            {
                var completed = new TaskCompletionSource();
                view.CoreWebView2.NavigationCompleted += Loaded;
                try
                {
                    Invoke(host, "RequestNavigation", target, NavigationOrigin.AddressBar);
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await VerifyRendering(view);
                }
                finally { view.CoreWebView2.NavigationCompleted -= Loaded; }
                void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess && view.CoreWebView2.Source == destination) completed.TrySetResult();
                }
            }

            async Task VerifyRendering(WebView2 view)
            {
                host.UpdateLayout();
                Assert.Equal(Visibility.Visible, view.Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)host.FindName("BoundarySurface")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)host.FindName("StartSurface")).Visibility);
                Assert.True(view.ActualWidth > 0 && view.ActualHeight > 0);
                Assert.False(view.CoreWebView2.IsSuspended);
                Assert.Equal("\"Account fixture\"", await view.CoreWebView2.ExecuteScriptAsync("document.querySelector('h1').textContent"));
                using var image = new MemoryStream();
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                image.Position = 0;
                var bitmap = BitmapDecoder.Create(image, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                var stride = (bitmap.PixelWidth * bitmap.Format.BitsPerPixel + 7) / 8;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);
                Assert.True(pixels.Count(value => value > 240) > pixels.Length / 2, "The white account fixture must be painted, not only present in the DOM.");
            }
        }
        finally
        {
            host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    private static object? Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static async Task Until(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Tab rendering did not reach its expected state."); await Task.Delay(20); }
    }
    private sealed class Policy : ISitePolicySource
    {
        internal bool AllowIdentity;
        public bool TryGetActivePolicy(out SitePolicySnapshot snapshot)
        {
            var entries = new List<SitePolicyEntry> { new("127.0.0.1", AccessClass.Whitelist), new("127.0.0.3", AccessClass.Blacklist) };
            if (AllowIdentity) entries.Add(new("127.0.0.2", AccessClass.Whitelist));
            snapshot = new(entries);
            return true;
        }
    }
}
