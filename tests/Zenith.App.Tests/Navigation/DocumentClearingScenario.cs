using System.Reflection;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

internal static class DocumentClearingScenario
{
    internal static async Task RunAsync()
    {
        foreach (var failure in new[] { "success", "failed", "cancelled", "hung", "renderer-crash" })
            await RunAsync(failure);
    }

    private static async Task RunAsync(string failure)
    {
        await using var site = new LoopbackSite("127.0.0.1");
        site.Response = _ => (200, "Set-Cookie: fixture-session=synthetic; HttpOnly; SameSite=Strict\r\n",
            "<html><body>Authenticated fixture content</body></html>");
        var folder = Directory.CreateTempSubdirectory("Zenith-DocumentClearing-");
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist)])));
        var window = new MainWindow(new NavigationCoordinator(evaluator))
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Console.WriteLine("Document clearing scenario: " + failure);
            window.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            await UntilAsync(() => Get<bool>(tab, "IsReady"));
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); };
            Invoke(window, "RequestNavigation", site.Origin + "/account", NavigationOrigin.AddressBar);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Contains("Authenticated fixture", await core.ExecuteScriptAsync("document.body.textContent"));
            var oldUri = Get<Uri>(tab, "CurrentUri");
            var cancelled = false;
            var crashed = false;
            core.ProcessFailed += (_, e) => crashed |= e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited;
            if (failure == "cancelled")
                browser.NavigationStarting += (_, e) =>
                {
                    if (e.Uri == "about:blank") { cancelled = true; e.Cancel = true; }
                };

            Invoke(window, "ReturnToSphere");
            Assert.False(Get<bool>(tab, "IsStartSurface"));
            Assert.Equal(oldUri, Get<Uri>(tab, "CurrentUri"));
            Assert.Equal(Visibility.Collapsed, browser.Visibility);
            var clear = Get<DocumentClearance>(tab, "Clearance");
            var operations = Get<NavigationOperationTracker>(tab, "NavigationOperations");
            Assert.True(clear.IsPending);

            // Reserve issuance without delivering a completion to model a hung
            // engine. The real production deadline must dispose the real controller.
            if (failure is "hung" or "renderer-crash") Assert.True(operations.TryIssueInternalClear());
            if (failure == "failed") clear.Issue(() => throw new InvalidOperationException("Injected Navigate failure"));
            if (failure == "renderer-crash") _ = CrashRendererAsync(core);

            if (failure != "failed")
            {
                Invoke(window, "ActivateTab", tab);
                Assert.Equal(Visibility.Collapsed, browser.Visibility);
                // Requests queued during removal cannot skip it or revive a failed view.
                Invoke(window, "RequestNavigation", site.Origin + "/next", NavigationOrigin.AddressBar);
            }

            if (failure == "success")
            {
                await UntilAsync(() => !clear.IsPending && core.Source == site.Origin + "/next");
                Assert.False(Get<bool>(tab, "ControllerDestroyed"));
                Assert.Equal(Visibility.Visible, browser.Visibility);
            }
            else
            {
                await UntilAsync(() => Get<bool>(tab, "ControllerDestroyed"));
                Assert.True(Get<bool>(tab, "IsStartSurface"));
                Assert.Null(Get<object?>(tab, "CurrentUri"));
                Assert.False(Get<bool>(tab, "IsReady"));
                Assert.False(clear.IsPending);
                Assert.Throws<ObjectDisposedException>(() => browser.CoreWebView2);
                Assert.DoesNotContain("/next", site.Requests);
                if (failure == "cancelled") Assert.True(cancelled);
                if (failure == "renderer-crash") Assert.True(crashed);
                Invoke(window, "ActivateTab", tab);
                Assert.Equal(Visibility.Collapsed, browser.Visibility);
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Document clearing scenario failed: " + failure, exception);
        }
        finally
        {
            window.Close();
            try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            try { folder.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task CrashRendererAsync(CoreWebView2 core)
    {
        // Test-only CDP crash. Production recovery uses the supported ProcessFailed event.
        try { await core.CallDevToolsProtocolMethodAsync("Page.crash", "{}").WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* A crashed renderer cannot reply to its CDP command. */ }
    }

    private static T Get<T>(object target, string property) => (T)target.GetType().GetProperty(property)!.GetValue(target)!;
    private static void Invoke(MainWindow window, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
}
