using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.App.Settings;
using Zenith.App.Access;
using Zenith.Core.Access;
using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Navigation;

public sealed class WebViewNavigationTests
{
    [WebViewFact]
    [Trait("Category", "WebView2")]
    public async Task BrowserSettingsAndGreylistFlowRespectNavigationAndAccessBoundaries()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application
            {
                Resources = LoadTheme(),
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await ExerciseWindowAsync();
                    await NetworkEnforcementScenario.RunAsync();
                    await SecurityHardeningScenario.RunAsync();
                    finished.TrySetResult();
                }
                catch (Exception exception)
                {
                    finished.TrySetException(exception);
                }
                finally
                {
                    app.Shutdown();
                }
            });
            app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(150));
    }

    private static async Task ExerciseWindowAsync()
    {
        var profile = Directory.CreateTempSubdirectory("Zenith-WebViewTests-");
        var clock = new AccessTestClock();
        using var accessStore = new ProtectedAccessStore(Path.Combine(profile.FullName, "Access"));
        var blacklist = new TestBlacklist();
        var policy = new VaultService(accessStore, clock, blacklist);
        var access = new GreylistAccessService(policy, accessStore, accessStore, clock);
        var coordinator = new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy, access, clock));
        using var filterHttp = new System.Net.Http.HttpClient(new AdblockWebViewFixture());
        using var adblock = new Zenith.App.Filtering.AdblockService(Path.Combine(profile.FullName, "Filtering"), filterHttp);
        await adblock.UpdateAsync();
        Assert.DoesNotContain("failed", adblock.Status);
        Assert.Contains(".zenith-test-ad", adblock.Cosmetics("https://github.com/filter-test", ["zenith-test-ad"], [], []));
        var window = new MainWindow(coordinator, access, policy, blacklist, adblock)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            Opacity = 0
        };
        var browser = (WebView2)window.FindName("Browser");
        var boundary = (FrameworkElement)window.FindName("BoundarySurface");
        var boundaryTarget = (TextBlock)window.FindName("BoundaryTargetTextBlock");
        browser.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = profile.FullName
        };
        var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreWebView2Environment? environment = null;

        try
        {
            window.Show();
            Console.WriteLine("WebView regression: initializing");
            await browser.EnsureCoreWebView2Async().WaitAsync(TimeSpan.FromSeconds(20));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var core = browser.CoreWebView2;
            await WaitUntilAsync(() =>
            {
                var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                return (bool)tab.GetType().GetProperty("IsReady")!.GetValue(tab)!;
            });
            environment = core.Environment;
            environment.BrowserProcessExited += (_, _) => browserExited.TrySetResult();
            var requestedAddresses = new List<string>();
            var filteredAddresses = new List<string>();
            var trace = new List<string>();
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Response is not null)
                {
                    Assert.Equal(403, e.Response.StatusCode);
                    filteredAddresses.Add(e.Request.Uri);
                    return;
                }
                requestedAddresses.Add(e.Request.Uri);
                var request = new Uri(e.Request.Uri);
                if (request.AbsolutePath == "/filter-test")
                {
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(AdblockWebViewFixture.Page(request.Host))),
                        200, "OK", "Content-Type: text/html\r\nCache-Control: no-store\r\nContent-Security-Policy: style-src 'none'");
                    return;
                }
                if (request.Host == "github.com" && request.AbsolutePath == "/redirect-test")
                {
                    e.Response = environment.CreateWebResourceResponse(null, 302, "Found",
                        $"Location: {Uri.UnescapeDataString(request.Query[1..])}\r\nCache-Control: no-store");
                    return;
                }
                // The actual renderer and window run, but every page response is local.
                var body = request.Host == "embedded.example" && request.AbsolutePath == "/widget"
                    ? "<html><body>Embedded widget<script>parent.postMessage('zenith-widget-ready', 'https://github.com');</script></body></html>"
                    : "<html><head><title>Zenith navigation test</title></head><body>Loaded</body></html>";
                e.Response = environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(body)),
                    200, "OK", "Content-Type: text/html\r\nCache-Control: no-store");
            };
            core.NavigationStarting += (_, e) => trace.Add($"Starting {e.NavigationId} {e.Uri} cancel={e.Cancel}");
            core.HistoryChanged += (_, _) => trace.Add($"History {core.Source}");
            core.NavigationCompleted += (_, e) => trace.Add($"Completed {e.NavigationId} {e.IsSuccess} {core.Source}");
            var blankBoundaries = 0;
            var visibility = DependencyPropertyDescriptor.FromProperty(
                UIElement.VisibilityProperty, boundary.GetType());
            EventHandler boundaryChanged = (_, _) =>
            {
                if (boundary.Visibility == Visibility.Visible)
                {
                    trace.Add($"Boundary {boundaryTarget.Text}");
                    if (NavigationOperationTracker.IsBlankTarget(core.Source))
                    {
                        blankBoundaries++;
                    }
                }
            };
            visibility.AddValueChanged(boundary, boundaryChanged);

            try
            {
                Assert.Equal("about:blank", core.Source);
                var firstLoad = WaitForPageAsync(core, "https://github.com/");
                Request(window, "https://github.com/");

                // Replay HistoryChanged at the vulnerable instant: the shell already
                // expects GitHub but WebView2 still reports its initial blank document.
                Assert.Equal("about:blank", core.Source);
                Invoke(window, "Browser_OnHistoryChanged", core, EventArgs.Empty);
                Assert.True(boundary.Visibility != Visibility.Visible,
                    $"First open produced a boundary for {boundaryTarget.Text}. {string.Join(" | ", trace)}");
                await firstLoad;
                Console.WriteLine("WebView regression: first page loaded");
                Assert.Equal(Visibility.Visible, browser.Visibility);
                Assert.Equal(0, blankBoundaries);

                // Same-document address changes must still update the real shell.
                await core.ExecuteScriptAsync("history.pushState({}, '', '/navigation-test#section')");
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                Assert.Null(window.FindName("CurrentSiteButton"));
                string TabTooltip() => ((StackPanel)window.FindName("OpenTabsPanel")).Children
                    .OfType<Grid>().SelectMany(row => row.Children.OfType<Button>())
                    .Select(button => button.ToolTip?.ToString()).First(text => text?.Contains("https://github.com/") == true)!;
                Assert.Contains("https://github.com/navigation-test#section", TabTooltip());

                var secondPage = WaitForPageAsync(core, "https://github.com/second");
                Request(window, "https://github.com/second");
                await secondPage;
                var backButton = (Button)window.FindName("BackButton");
                Assert.True(backButton.IsEnabled);
                var back = WaitForPageAsync(core, "https://github.com/navigation-test#section");
                backButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await back;
                Assert.Contains("https://github.com/navigation-test#section", TabTooltip());
                var forwardButton = (Button)window.FindName("ForwardButton");
                Assert.True(forwardButton.IsEnabled);
                var forward = WaitForPageAsync(core, "https://github.com/second");
                forwardButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await forward;
                Assert.Contains("https://github.com/second", TabTooltip());

                // Reopen after native cleanup, including a request while clear is issued.
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    Invoke(window, "ReturnToSphere");
                    if (attempt % 2 == 1)
                    {
                        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                    var target = $"https://www.wikipedia.org/?attempt={attempt}";
                    var reopened = WaitForPageAsync(core, target);
                    Request(window, target);
                    await reopened;
                    Assert.Equal(Visibility.Visible, browser.Visibility);
                    Assert.NotEqual(Visibility.Visible, boundary.Visibility);
                }
                Assert.Equal(0, blankBoundaries);

                Request(window, "https://outside.example/");
                Assert.Equal(Visibility.Visible, boundary.Visibility);
                Assert.Equal("https://outside.example/", boundaryTarget.Text);
                Assert.DoesNotContain(requestedAddresses, address => address.Contains("outside.example"));

                // Only an observed blank source is ignored; a typed unsupported
                // address still receives the ordinary Core denial.
                Request(window, "about:blank");
                Assert.Equal(Visibility.Visible, boundary.Visibility);
                Assert.Equal("about:blank", boundaryTarget.Text);
                var afterBoundary = WaitForPageAsync(core, "https://github.com/after-boundary");
                Request(window, "https://github.com/after-boundary");
                await afterBoundary;
                Assert.Equal(Visibility.Visible, browser.Visibility);
                Assert.NotEqual(Visibility.Visible, boundary.Visibility);

                await ExerciseRedirectsAndPopupsAsync(window, browser, requestedAddresses);
                Console.WriteLine("WebView regression: redirects passed");
                await ExerciseEmbeddedCompatibilityAsync(window, browser, access);
                Console.WriteLine("WebView regression: embedding passed");
                await ExerciseBlacklistResourcesAsync(window, browser, blacklist, filteredAddresses, requestedAddresses);
                Console.WriteLine("WebView regression: blacklist passed");
                await ExerciseAdblockAsync(window, browser, filteredAddresses, requestedAddresses);
                Console.WriteLine("WebView regression: adblock passed");
                await ExerciseSphereScrollbarAsync(window);
                await ExerciseCapabilitiesAsync(core);
                await ExerciseBackgroundTabAsync(window, browser);
                await ExerciseSettingsAsync(window, browser, profile.FullName);
                await ExerciseAccessAsync(window, browser, access, clock, profile.FullName);
                await ExerciseVaultAsync(window, browser, accessStore, policy, access, clock, profile.FullName);
            }
            finally
            {
                visibility.RemoveValueChanged(boundary, boundaryChanged);
            }
        }
        finally
        {
            window.Close();
            if (environment is not null)
            {
                await browserExited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            accessStore.Dispose();
            profile.Delete(recursive: true);
        }
    }

    private static async Task WaitForPageAsync(CoreWebView2 core, string target)
    {
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && core.Source == target)
            {
                loaded.TrySetResult();
            }
        }
        core.NavigationCompleted += Completed;
        try
        {
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            core.NavigationCompleted -= Completed;
        }
    }

    private static async Task ExerciseAdblockAsync(MainWindow window, WebView2 browser, List<string> filtered, List<string> requested)
    {
        var core = browser.CoreWebView2;
        var frames = new List<CoreWebView2Frame>();
        void Created(object? sender, CoreWebView2FrameCreatedEventArgs e)
        {
            frames.Add(e.Frame);
            e.Frame.FrameCreated += Created;
        }
        core.FrameCreated += Created;
        try
        {
            var load = WaitForPageAsync(core, "https://github.com/filter-test");
            Request(window, "https://github.com/filter-test");
            await load;
            await WaitForScriptAsync(core.ExecuteScriptAsync, "getComputedStyle(document.getElementById('advert')).display === 'none'");
            Assert.Equal("true", await core.ExecuteScriptAsync("getComputedStyle(document.getElementById('exception')).display !== 'none' && getComputedStyle(document.getElementById('content')).display !== 'none'"));
            await WaitForScriptAsync(core.ExecuteScriptAsync, "getComputedStyle(document.getElementById('scoped')).display === 'none'");
            await core.ExecuteScriptAsync("const dynamicAd = document.createElement('div'); dynamicAd.id = 'dynamic-ad'; document.body.appendChild(dynamicAd); dynamicAd.className = 'zenith-test-ad';");
            await WaitForScriptAsync(core.ExecuteScriptAsync, "getComputedStyle(document.getElementById('dynamic-ad')).display === 'none'");
            await WaitUntilAsync(() => frames.Any(frame => frame.Name == "cosmetic-nested"));
            var child = frames.Single(frame => frame.Name == "cosmetic-child");
            var nested = frames.Single(frame => frame.Name == "cosmetic-nested");
            await WaitForScriptAsync(child.ExecuteScriptAsync, "getComputedStyle(document.getElementById('scoped')).display === 'none' && getComputedStyle(document.getElementById('exception')).display === 'none'");
            await child.ExecuteScriptAsync("const late = document.createElement('div'); late.id = 'late-ad'; document.body.appendChild(late); late.className = 'zenith-dynamic-ad';");
            await WaitForScriptAsync(child.ExecuteScriptAsync, "getComputedStyle(document.getElementById('late-ad')).display === 'none'");
            await WaitForScriptAsync(nested.ExecuteScriptAsync, "getComputedStyle(document.getElementById('advert')).display === 'none'");
            Assert.Equal("true", await nested.ExecuteScriptAsync("getComputedStyle(document.getElementById('scoped')).display !== 'none'"));
            await core.ExecuteScriptAsync("""
                fetch('https://ads.zenith-test.example/data').catch(() => {});
                fetch('https://ads.zenith-test.example/allowed').catch(() => {});
                fetch('https://blocked.example/ad-exception').catch(() => {});
                const image = new Image(); image.src = 'https://ads.zenith-test.example/image.png'; document.body.appendChild(image);
                const frame = document.createElement('iframe'); frame.src = 'https://ads.zenith-test.example/frame'; document.body.appendChild(frame);
                """);
            Console.WriteLine("WebView regression: cosmetics and nested frames passed");
            await WaitUntilAsync(() => filtered.Contains("https://ads.zenith-test.example/data") && filtered.Contains("https://ads.zenith-test.example/image.png")
                && filtered.Contains("https://ads.zenith-test.example/frame") && filtered.Contains("https://blocked.example/ad-exception"));
            Assert.DoesNotContain("https://ads.zenith-test.example/data", requested);
            Assert.Contains("https://ads.zenith-test.example/allowed", requested);
            var nextLoad = WaitForPageAsync(core, "https://github.com/after-filter-test");
            Request(window, "https://github.com/after-filter-test");
            await nextLoad;
            Assert.Equal("0", await core.ExecuteScriptAsync("document.querySelectorAll('#advert, #dynamic-ad').length"));
            await core.ExecuteScriptAsync("const fresh = document.createElement('div'); fresh.id = 'fresh-ad'; fresh.className = 'zenith-test-ad'; document.body.appendChild(fresh);");
            await WaitForScriptAsync(core.ExecuteScriptAsync, "getComputedStyle(document.getElementById('fresh-ad')).display === 'none'");
        }
        finally
        {
            core.FrameCreated -= Created;
            foreach (var frame in frames.Where(frame => frame.IsDestroyed() == 0)) frame.FrameCreated -= Created;
        }
    }

    private static async Task WaitForScriptAsync(Func<string, Task<string>> execute, string expression)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await execute(expression) == "true") return;
            await Task.Delay(100);
        }
        Assert.Fail("Cosmetic condition did not become true: " + expression + " | " + await execute("JSON.stringify({url:location.href,sheets:document.adoptedStyleSheets.map(s => [...s.cssRules].map(r => r.cssText).filter(t => t.includes('zenith'))), dom:document.body.innerHTML})"));
    }

    private static async Task ExerciseRedirectsAndPopupsAsync(MainWindow window, WebView2 browser,
        List<string> requestedAddresses)
    {
        var core = browser.CoreWebView2;
        var boundary = (FrameworkElement)window.FindName("BoundarySurface");
        var boundaryTarget = (TextBlock)window.FindName("BoundaryTargetTextBlock");
        var tabs = (System.Collections.IEnumerable)typeof(MainWindow)
            .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var cancelledTargets = new HashSet<string>();
        void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.Cancel) cancelledTargets.Add(e.Uri);
        }
        core.NavigationStarting += NavigationStarting;
        string Redirect(string target) => "https://github.com/redirect-test?" + Uri.EscapeDataString(target);
        async Task OpenAllowed(string target)
        {
            var loaded = WaitForPageAsync(core, target);
            Request(window, target);
            await loaded;
        }
        async Task WaitForDenial(string target)
        {
            await WaitUntilAsync(() => boundary.Visibility == Visibility.Visible && boundaryTarget.Text == target);
            await WaitUntilAsync(() => core.Source == "about:blank");
            Assert.Equal(Visibility.Collapsed, browser.Visibility);
            // Cancellation prevents commitment, not necessarily the initial GET.
            // WebView2 can raise WebResourceRequested before navigation cancellation.
        }

        const string allowed = "https://learn.microsoft.com/redirect-allowed";
        var allowedLoad = WaitForPageAsync(core, allowed);
        Request(window, Redirect(allowed));
        await allowedLoad;
        Assert.Equal(Visibility.Visible, browser.Visibility);

        foreach (var denied in new[] { "https://outside.example/redirect-denied", "https://github.com.evil.example/redirect-denied" })
        {
            Request(window, Redirect(Redirect(denied)));
            await WaitForDenial(denied);
            Assert.Contains(denied, cancelledTargets);
            await OpenAllowed("https://github.com/after-redirect");
        }

        const string scriptTarget = "https://outside.example/script-redirect";
        await core.ExecuteScriptAsync($"location.replace('{scriptTarget}');");
        await WaitForDenial(scriptTarget);
        Assert.Contains(scriptTarget, cancelledTargets);
        await OpenAllowed("https://github.com/before-popup");

        var originalCount = tabs.Cast<object>().Count();
        const string popupTarget = "https://outside.example/popup-denied";
        await core.ExecuteScriptAsync($"window.open('{popupTarget}', '_blank');");
        await WaitForDenial(popupTarget);
        Assert.DoesNotContain(popupTarget, requestedAddresses);
        Assert.Equal(originalCount, tabs.Cast<object>().Count());
        await OpenAllowed("https://github.com/after-popup");

        // Unsupported popup targets must not get an unmanaged renderer window.
        await core.ExecuteScriptAsync("window.open('about:blank', '_blank');");
        await WaitForDenial("about:blank");
        Assert.Equal(originalCount, tabs.Cast<object>().Count());
        await OpenAllowed("https://github.com/after-hardening-checks");
        core.NavigationStarting -= NavigationStarting;
    }

    private sealed class TestBlacklist : Zenith.Core.Filtering.IBlacklistSource
    {
        public Zenith.Core.Filtering.HostsBlacklist? Current { get; set; } =
            Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example");
    }

    private static async Task ExerciseBlacklistResourcesAsync(MainWindow window, WebView2 browser,
        TestBlacklist source, List<string> filtered, List<string> permitted)
    {
        var core = browser.CoreWebView2;
        await core.ExecuteScriptAsync("""
            fetch('https://blocked.example/data').catch(() => {});
            const deniedFrame = document.createElement('iframe');
            deniedFrame.src = 'https://blocked.example/frame'; document.body.append(deniedFrame);
            const deniedImage = document.createElement('img');
            deniedImage.src = 'https://blocked.example/pixel'; document.body.append(deniedImage);
            """);
        await WaitUntilAsync(() => filtered.Contains("https://blocked.example/data") &&
            filtered.Contains("https://blocked.example/frame") && filtered.Contains("https://blocked.example/pixel"));
        Assert.DoesNotContain(permitted, address => new Uri(address).Host == "blocked.example");
        Assert.Equal(Visibility.Visible, browser.Visibility);
        source.Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example github.com");
        window.ApplyBlacklistUpdate();
        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        await WaitUntilAsync(() => core.Source == "about:blank");
        Request(window, "https://github.com/");
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
        source.Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example");
        window.ApplyBlacklistUpdate();
        const string target = "https://github.com/after-blacklist-update";
        var loaded = WaitForPageAsync(core, target);
        Request(window, target);
        await loaded;
    }

    private static async Task ExerciseEmbeddedCompatibilityAsync(MainWindow window, WebView2 browser,
        GreylistAccessService access)
    {
        const string parent = "https://github.com/embedded-compatibility";
        const string embedded = "https://embedded.example/widget";
        var core = browser.CoreWebView2;
        var load = WaitForPageAsync(core, parent);
        Request(window, parent);
        await load;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Message(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (e.Source == parent && e.TryGetWebMessageAsString() == "zenith-widget-ready") ready.TrySetResult();
        }
        core.WebMessageReceived += Message;
        try
        {
            await core.ExecuteScriptAsync("""
                window.addEventListener('message', e => {
                    if (e.origin === 'https://embedded.example' && e.data === 'zenith-widget-ready')
                        window.chrome.webview.postMessage(e.data);
                });
                const frame = document.createElement('iframe');
                frame.src = 'https://embedded.example/widget';
                document.body.append(frame);
                """);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(parent, core.Source);
            Assert.Equal(Visibility.Visible, browser.Visibility);
            Assert.Empty(access.GetPendingRequests());
            Assert.True(SiteIdentity.TryCreate("embedded.example", out var site));
            Assert.False(access.TryGetGrant(site!, out _));
        }
        finally { core.WebMessageReceived -= Message; }

        // Loading inside a Sphere page must not authorize opening the same URL directly.
        Request(window, embedded);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
        Assert.Equal(embedded, ((TextBlock)window.FindName("BoundaryTargetTextBlock")).Text);
        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        await WaitUntilAsync(() => core.Source == "about:blank");
        var reopen = WaitForPageAsync(core, parent);
        Request(window, parent);
        await reopen;
    }

    private static async Task ExerciseSphereScrollbarAsync(MainWindow window)
    {
        Invoke(window, "FocusSphereSearch");
        var popup = (System.Windows.Controls.Primitives.Popup)window.FindName("SphereResultsPopup");
        popup.IsOpen = true;
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        var scroll = Descendants<ScrollViewer>(content).First();
        Assert.True(scroll.ScrollableHeight > 0);
        var bar = Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroll)
            .Single(item => item.Orientation == Orientation.Vertical);
        Assert.Equal(8, bar.Width);
        Assert.Equal(0.4, bar.Opacity);
        scroll.ScrollToEnd();
        content.UpdateLayout();
        Assert.True(scroll.VerticalOffset > 0);
        scroll.ScrollToTop();
        content.UpdateLayout();
        if (Environment.GetEnvironmentVariable("ZENITH_SPHERE_SCREENSHOT") is { Length: > 0 } screenshot)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
                (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(screenshot);
            encoder.Save(stream);
        }
        ((TextBox)window.FindName("SphereSearchTextBox")).Text = "github";
        content.UpdateLayout();
        Assert.Equal(0, scroll.ScrollableHeight);
        Assert.Equal(Visibility.Collapsed, scroll.ComputedVerticalScrollBarVisibility);
        popup.IsOpen = false;
        ((TextBox)window.FindName("SphereSearchTextBox")).Text = string.Empty;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static async Task ExerciseCapabilitiesAsync(CoreWebView2 core)
    {
        var permission = new TaskCompletionSource<CoreWebView2PermissionRequestedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = new TaskCompletionSource<CoreWebView2DownloadStartingEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Permission(object? sender, CoreWebView2PermissionRequestedEventArgs e) => permission.TrySetResult(e);
        void Download(object? sender, CoreWebView2DownloadStartingEventArgs e) => download.TrySetResult(e);
        core.PermissionRequested += Permission;
        core.DownloadStarting += Download;
        try
        {
            await core.ExecuteScriptAsync("navigator.geolocation.getCurrentPosition(() => {}, () => {});");
            var request = await permission.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(CoreWebView2PermissionState.Deny, request.State);
            Assert.True(request.Handled);
            Assert.False(request.SavesInProfile);
            await core.ExecuteScriptAsync("const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob(['test'])); a.download = 'zenith-test.txt'; document.body.append(a); a.click(); a.remove();");
            var file = await download.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(file.Cancel);
            Assert.True(file.Handled);
        }
        finally
        {
            core.PermissionRequested -= Permission;
            core.DownloadStarting -= Download;
        }
    }

    private static async Task ExerciseBackgroundTabAsync(MainWindow window, WebView2 browser)
    {
        var originalSource = browser.CoreWebView2.Source;
        var tabs = (System.Collections.IEnumerable)typeof(MainWindow)
            .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var originalTab = tabs.Cast<object>().First();
        const string target = "https://github.com/background-tab";
        var creation = (Task)typeof(MainWindow).GetMethod("CreateTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [new Uri(target)])!;
        var newBrowser = ((Panel)window.FindName("BrowserHost")).Children.OfType<WebView2>().Last();
        var newTab = tabs.Cast<object>().Last();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        newBrowser.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) { loaded.TrySetException(e.InitializationException); return; }
            var core = newBrowser.CoreWebView2;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, request) => request.Response ??= core.Environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Background tab</body></html>")),
                200, "OK", "Content-Type: text/html\r\nCache-Control: no-store");
            core.NavigationCompleted += (_, navigation) =>
            {
                if (navigation.IsSuccess && core.Source == target) loaded.TrySetResult();
            };
        };
        Invoke(window, "ActivateTab", originalTab);
        await creation;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(originalSource, browser.CoreWebView2.Source);
        Assert.Equal(target, newBrowser.CoreWebView2.Source);
        Assert.Equal(Visibility.Collapsed, newBrowser.Visibility);
        Assert.Equal(Visibility.Visible, browser.Visibility);
        await ExerciseCapabilitiesAsync(newBrowser.CoreWebView2);
        var blockedRequest = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adRequest = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Resource(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (e.Request.Uri == "https://blocked.example/new-tab") blockedRequest.TrySetResult(e.Response?.StatusCode ?? 0);
            if (e.Request.Uri == "https://ads.zenith-test.example/new-tab") adRequest.TrySetResult(e.Response?.StatusCode ?? 0);
        }
        newBrowser.CoreWebView2.WebResourceRequested += Resource;
        try
        {
            Invoke(window, "ActivateTab", newTab);
            newBrowser.CoreWebView2.Resume();
            await newBrowser.CoreWebView2.ExecuteScriptAsync("fetch('https://blocked.example/new-tab').catch(() => {});");
            Assert.Equal(403, await blockedRequest.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            await newBrowser.CoreWebView2.ExecuteScriptAsync("fetch('https://ads.zenith-test.example/new-tab').catch(() => {}); const ad = document.createElement('div'); ad.id = 'new-tab-ad'; ad.className = 'zenith-test-ad'; document.body.appendChild(ad);");
            Assert.Equal(403, await adRequest.Task.WaitAsync(TimeSpan.FromSeconds(15)));
            await WaitForScriptAsync(newBrowser.CoreWebView2.ExecuteScriptAsync, "getComputedStyle(document.getElementById('new-tab-ad')).display === 'none'");
        }
        finally
        {
            newBrowser.CoreWebView2.WebResourceRequested -= Resource;
            Invoke(window, "ActivateTab", originalTab);
        }
        Invoke(window, "CloseTab", newTab);
    }

    private static async Task ExerciseSettingsAsync(MainWindow window, WebView2 browser, string profilePath)
    {
        var sourceBeforeSettings = browser.CoreWebView2.Source;
        var store = new BrowserPreferencesStore(Path.Combine(profilePath, "settings-test.json"));
        Uri? selectedSite = null;
        var settings = new SettingsWindow(store, new BrowserPreferences(),
            preferences => Invoke(window, "ApplyPreferences", preferences),
            DevelopmentStarterPolicy.Sites, target => selectedSite = target)
        {
            Owner = window,
            Opacity = 0,
            ShowActivated = false
        };
        try
        {
            settings.Show();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)settings.FindName("GeneralPage")).Visibility);
            ((RadioButton)settings.FindName("CompactOption")).IsChecked = true;
            ((Button)settings.FindName("ZoomInButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new BrowserPreferences(false, 110), store.Load());
            Assert.Equal(1.1, browser.ZoomFactor);
            Assert.Equal(sourceBeforeSettings, browser.CoreWebView2.Source);

            settings.SelectSection("Sphere");
            ((TextBox)settings.FindName("SiteFilter")).Text = "git";
            var matchingSites = ((ItemsControl)settings.FindName("SiteList")).Items.Cast<StarterWhitelistSite>().ToArray();
            Assert.Contains(matchingSites, site => site.Host == "github.com");
            Assert.Contains(matchingSites, site => site.Host == "gitlab.com");
            Assert.All(matchingSites, site => Assert.True(
                site.Name.Contains("git", StringComparison.OrdinalIgnoreCase) ||
                site.Host.Contains("git", StringComparison.OrdinalIgnoreCase)));
            ((TextBox)settings.FindName("SiteFilter")).Text = "outside.example";
            Assert.Empty(((ItemsControl)settings.FindName("SiteList")).Items);
            Assert.Null(selectedSite);

            foreach (var section in new[] { "Access", "Vault", "About", "General" })
            {
                settings.SelectSection(section);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)settings.FindName($"{section}Page")).Visibility);
            }

            if (Environment.GetEnvironmentVariable("ZENITH_SETTINGS_SCREENSHOT") is { Length: > 0 } screenshot)
            {
                settings.UpdateLayout();
                var content = (FrameworkElement)settings.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight,
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(screenshot);
                encoder.Save(stream);
            }
        }
        finally
        {
            settings.Close();
        }
        Assert.Equal(sourceBeforeSettings, browser.CoreWebView2.Source);
        Assert.Equal(Visibility.Visible, browser.Visibility);
    }

    private static void Request(MainWindow window, string target) =>
        Invoke(window, "RequestNavigation", target, NavigationOrigin.AddressBar);

    private static void Invoke(MainWindow window, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, arguments);

    private static async Task ExerciseAccessAsync(MainWindow window, WebView2 browser,
        GreylistAccessService service, AccessTestClock clock, string profilePath)
    {
        const string password = "a deliberately long UI test password";
        var target = new Uri("https://outside.example/article");
        Request(window, target.AbsoluteUri);
        Assert.Equal(Visibility.Visible, ((Button)window.FindName("RequestAccessButton")).Visibility);
        var gate = new AccessWindow(service, target, uri => Request(window, uri.AbsoluteUri))
        {
            Owner = window, Opacity = 0, ShowActivated = false
        };
        try
        {
            gate.Show();
            ((PasswordBox)gate.FindName("Password")).Password = password;
            ((PasswordBox)gate.FindName("Confirmation")).Password = password;
            ((Button)gate.FindName("SubmitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => service.ConfigurationState == AccessConfigurationState.Ready &&
                ((Button)gate.FindName("SubmitButton")).IsEnabled);
            Assert.Equal(AccessPhase.FirstChallenge, service.GetStatus(target.AbsoluteUri).Phase);
            Assert.Empty(((PasswordBox)gate.FindName("Password")).Password);

            ((PasswordBox)gate.FindName("Password")).Password = password;
            ((Button)gate.FindName("SubmitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => service.GetStatus(target.AbsoluteUri).Phase == AccessPhase.Cooldown &&
                ((FrameworkElement)gate.FindName("PasswordPanel")).Visibility == Visibility.Collapsed);
        }
        finally { gate.Close(); }

        Uri? requestedVisit = null;
        var settings = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(profilePath, "access-settings.json")),
            new BrowserPreferences(), _ => { }, DevelopmentStarterPolicy.Sites, _ => { }, service, uri => requestedVisit = uri)
        {
            Owner = window, Opacity = 0, ShowActivated = false
        };
        try
        {
            settings.Show();
            settings.SelectSection("Access");
            Assert.Single(((ItemsControl)settings.FindName("PendingRequests")).Items);
            Assert.Equal(Visibility.Collapsed, ((Button)settings.FindName("SetupPasswordButton")).Visibility);
            var sourceBeforeRequest = browser.CoreWebView2.Source;
            var address = (TextBox)settings.FindName("AccessAddress");
            var begin = (Button)settings.FindName("BeginAccessButton");
            foreach (var invalid in new[] { "", "a search query", "file:///C:/test", "https://user:secret@example.com/", "https://github.com/" })
            {
                address.Text = invalid;
                begin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(requestedVisit);
                Assert.True(settings.IsVisible);
                Assert.NotEmpty(((TextBlock)settings.FindName("AccessAddressFeedback")).Text);
            }
            address.Text = "NEW-GREY.example/path";
            Assert.Empty(((TextBlock)settings.FindName("AccessAddressFeedback")).Text);
            begin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new Uri("https://new-grey.example/path"), requestedVisit);
            Assert.False(settings.IsVisible);
            Assert.Equal(AccessPhase.FirstChallenge, service.GetStatus(requestedVisit!.AbsoluteUri).Phase);
            Assert.Single(service.GetPendingRequests());
            Assert.Equal(sourceBeforeRequest, browser.CoreWebView2.Source);
        }
        finally { settings.Close(); }

        clock.Advance(TimeSpan.FromSeconds(5));
        var confirmation = new AccessWindow(service, target, uri => Request(window, uri.AbsoluteUri))
        {
            Owner = window, Opacity = 0, ShowActivated = false
        };
        try
        {
            confirmation.Show();
            Assert.Equal(AccessPhase.SecondChallenge, service.GetStatus(target.AbsoluteUri).Phase);
            var load = WaitForPageAsync(browser.CoreWebView2, target.AbsoluteUri);
            ((PasswordBox)confirmation.FindName("Password")).Password = password;
            ((Button)confirmation.FindName("SubmitButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await load;
        }
        finally { confirmation.Close(); }
        Assert.Equal(Visibility.Visible, browser.Visibility);
        Assert.Equal(AccessPhase.Granted, service.GetStatus(target.AbsoluteUri).Phase);
        var activeSettings = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(profilePath, "active-settings.json")),
            new BrowserPreferences(), _ => { }, DevelopmentStarterPolicy.Sites, _ => { }, service)
        { Owner = window, Opacity = 0, ShowActivated = false };
        try
        {
            activeSettings.Show();
            activeSettings.SelectSection("Access");
            Assert.Single(((ItemsControl)activeSettings.FindName("ActiveVisits")).Items);
            Assert.Empty(((ItemsControl)activeSettings.FindName("PendingRequests")).Items);
        }
        finally { activeSettings.Close(); }
        // Temporary access must not promote a destination into ordinary discovery.
        var inSphere = typeof(MainWindow).GetMethod("IsTargetInSphere", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.False((bool)inSphere.Invoke(window, [target])!);
        Assert.False(((Button)window.FindName("BookmarkButton")).IsEnabled);
        var search = (TextBox)window.FindName("SphereSearchTextBox");
        search.Text = "outside.example";
        Invoke(window, "RefreshSphereResults");
        var results = (System.Collections.ICollection)typeof(MainWindow)
            .GetField("_visibleSphereResults", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.Empty(results.Cast<object>());

        // The same exact-host grant works in a second real tab, without touching
        // the normal profile. All tabs share the initial isolated environment.
        var createTab = (Task)typeof(MainWindow).GetMethod("CreateTabAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null])!;
        await createTab;
        var secondBrowser = ((Panel)window.FindName("BrowserHost")).Children.OfType<WebView2>().Last();
        var secondCore = secondBrowser.CoreWebView2;
        secondCore.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        secondCore.WebResourceRequested += (_, e) => e.Response ??= secondCore.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Second tab</body></html>")),
            200, "OK", "Content-Type: text/html\r\nCache-Control: no-store");
        var secondTarget = "https://outside.example/another-page";
        var secondLoad = WaitForPageAsync(secondCore, secondTarget);
        Request(window, secondTarget);
        await secondLoad;
        Assert.Equal(Visibility.Visible, secondBrowser.Visibility);
        Assert.Equal(Visibility.Collapsed, browser.Visibility);

        clock.Advance(TimeSpan.FromSeconds(5));
        // Activating the retained first tab must deny before showing its document,
        // even without waiting for the periodic expiry scan.
        var tabs = (System.Collections.IEnumerable)typeof(MainWindow)
            .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        Invoke(window, "ActivateTab", tabs.Cast<object>().First());
        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        window.ValidateRetainedTabs();
        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
        await WaitUntilAsync(() => browser.CoreWebView2.Source == "about:blank");
        await WaitUntilAsync(() => secondCore.Source == "about:blank");
        Assert.Equal(Visibility.Collapsed, secondBrowser.Visibility);
        Assert.Equal(AccessPhase.FirstChallenge, service.GetStatus(target.AbsoluteUri).Phase);

        // Also cover an actively displayed page expiring on the host timer path,
        // without a tab switch or another navigation request.
        service.SubmitPassword(target.AbsoluteUri, password);
        clock.Advance(TimeSpan.FromSeconds(5));
        service.SubmitPassword(target.AbsoluteUri, password);
        var renewedLoad = WaitForPageAsync(browser.CoreWebView2, target.AbsoluteUri);
        Request(window, target.AbsoluteUri);
        await renewedLoad;
        Assert.Equal(Visibility.Visible, browser.Visibility);
        clock.Advance(TimeSpan.FromSeconds(5));
        window.ValidateRetainedTabs();
        Assert.Equal(Visibility.Collapsed, browser.Visibility);
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
        await WaitUntilAsync(() => browser.CoreWebView2.Source == "about:blank");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) { throw new TimeoutException("The expected UI state did not arrive."); }
            await Task.Delay(20);
        }
    }

    private static async Task ExerciseVaultAsync(MainWindow window, WebView2 browser, ProtectedAccessStore store,
        VaultService vault, GreylistAccessService access, AccessTestClock clock, string profile)
    {
        const string oldPassword = "a deliberately long UI test password";
        const string newPassword = "a different and deliberate UI password";
        IReadOnlyList<StarterWhitelistSite> Sites() => (IReadOnlyList<StarterWhitelistSite>)typeof(MainWindow)
            .GetMethod("GetSphereSites", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null)!;
        Assert.Contains(Sites(), site => site.Host == "stackexchange.com");
        Assert.DoesNotContain(Sites(), site => site.Host == "math.stackexchange.com");
        Assert.DoesNotContain(Sites(), site => site.Host == "physics.stackexchange.com");
        var settings = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(profile, "vault-settings.json")),
            new(), _ => { }, Sites(), uri => Request(window, uri.AbsoluteUri), access, _ => { }, vault, Sites,
            () => Invoke(window, "RefreshPolicyViews")) { Owner = window, Opacity = 0, ShowActivated = false };
        try
        {
            settings.Show();
            settings.SelectSection("Vault");
            var panel = (VaultPanel)settings.FindName("VaultEditor");
            Assert.False(((CheckBox)panel.FindName("IncludeSubdomains")).IsChecked);
            ((CheckBox)panel.FindName("IncludeSubdomains")).IsChecked = true;
            ((TextBox)panel.FindName("GreySeconds")).Text = "10";
            ((TextBox)panel.FindName("GrantSeconds")).Text = "30";
            ((TextBox)panel.FindName("VaultSeconds")).Text = "10";
            ((TextBox)panel.FindName("SiteHost")).Text = "vault-added.example";
            ((CheckBox)panel.FindName("ChangePassword")).IsChecked = true;
            ((PasswordBox)panel.FindName("NewPassword")).Password = newPassword;
            ((PasswordBox)panel.FindName("RepeatPassword")).Password = newPassword;
            ((TextBox)panel.FindName("GrantSeconds")).Text = "1";
            ((ComboBox)panel.FindName("GrantUnit")).SelectedIndex = 1;
            ((Button)panel.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("1 minute", ((TextBlock)panel.FindName("ReviewSummary")).Text);
            ((TextBox)panel.FindName("GrantSeconds")).Text = "30";
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("ReviewCard")).Visibility);
            Assert.False(((Button)panel.FindName("StageButton")).IsEnabled);
            ((PasswordBox)panel.FindName("StagePassword")).Password = oldPassword;
            ((Button)panel.FindName("StageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(store.LoadVault().Pending);
            ((ComboBox)panel.FindName("GrantUnit")).SelectedIndex = 0;
            ((TextBox)panel.FindName("SiteHost")).Text = "https://invalid.example/path";
            ((Button)panel.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("ReviewCard")).Visibility);
            Assert.False(((Button)panel.FindName("StageButton")).IsEnabled);
            Assert.NotEmpty(((TextBlock)panel.FindName("OutcomeText")).Text);
            Assert.Null(store.LoadVault().Pending);
            ((TextBox)panel.FindName("SiteHost")).Text = "vault-added.example";
            ((Button)panel.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("5 seconds", ((TextBlock)panel.FindName("ReviewSummary")).Text);
            ((PasswordBox)panel.FindName("StagePassword")).Password = oldPassword;
            ((Button)panel.FindName("StageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => store.LoadVault().Pending is not null && ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
            Assert.Empty(((PasswordBox)panel.FindName("NewPassword")).Password);
            Assert.DoesNotContain(Sites(), site => site.Host == "vault-added.example");
            Assert.False(((Button)panel.FindName("ConfirmButton")).IsEnabled);

            if (Environment.GetEnvironmentVariable("ZENITH_SETTINGS_SCREENSHOT") is { Length: > 0 } screenshot)
            {
                ((ScrollViewer)settings.FindName("PageScrollViewer")).ScrollToTop();
                settings.UpdateLayout();
                var content = (FrameworkElement)settings.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(Path.GetDirectoryName(screenshot)!, "vault-preview.png"));
                encoder.Save(stream);
            }

            clock.Advance(TimeSpan.FromSeconds(5));
            panel.Refresh();
            Assert.True(((Button)panel.FindName("ConfirmButton")).IsEnabled);
            ((PasswordBox)panel.FindName("ConfirmPassword")).Password = oldPassword;
            ((Button)panel.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => store.LoadVault().Revision == 1 && ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
            Assert.True(store.Verify(newPassword));
            Assert.False(store.Verify(oldPassword));
            Assert.Equal(new VaultSettings(10, 30, 10), store.LoadVault().Settings);
            Assert.Contains(Sites(), site => site.Host == "vault-added.example");
            settings.SelectSection("Sphere");
            ((TextBox)settings.FindName("SiteFilter")).Text = "vault-added.example";
            Assert.Single(((ItemsControl)settings.FindName("SiteList")).Items);

            var load = WaitForPageAsync(browser.CoreWebView2, "https://vault-added.example/");
            Request(window, "https://vault-added.example/");
            await load;
            Assert.True(((Button)window.FindName("BookmarkButton")).IsEnabled);
            var subdomainLoad = WaitForPageAsync(browser.CoreWebView2, "https://sub.vault-added.example/");
            Request(window, "https://sub.vault-added.example/");
            await subdomainLoad;
            Assert.Equal(Visibility.Visible, browser.Visibility);
            var request = access.SubmitPassword("https://new-greylist.example/", newPassword);
            Assert.Equal(clock.GetUtcNow().AddSeconds(10), request.Status.EligibleAt);

            settings.SelectSection("Vault");
            var removeSite = (ListBox)panel.FindName("RemoveSite");
            static string ChoiceHost(object item) => (string)item.GetType().GetProperty("Host")!.GetValue(item)!;
            removeSite.SelectedItems.Add(removeSite.Items.Cast<object>().Single(site => ChoiceHost(site) == "vault-added.example"));
            removeSite.SelectedItems.Add(removeSite.Items.Cast<object>().Single(site => ChoiceHost(site) == "google.com"));
            var knownSite = (ComboBox)panel.FindName("KnownSite");
            foreach (var host in new[] { "scholar.google.com", "drive.google.com", "mail.google.com" })
            {
                knownSite.SelectedItem = knownSite.Items.Cast<object>().Single(site => ChoiceHost(site) == host);
                Assert.False(((CheckBox)panel.FindName("IncludeSubdomains")).IsChecked);
                ((Button)panel.FindName("AddSiteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            ((Button)panel.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Remove vault-added.example and its covered services",
                ((TextBlock)panel.FindName("ReviewSummary")).Text);
            Assert.Contains("Google Scholar", ((TextBlock)panel.FindName("ReviewSummary")).Text);
            Assert.DoesNotContain("scholar.google.com", ((TextBlock)panel.FindName("ReviewSummary")).Text);
            Assert.Contains("scholar.google.com", ((TextBlock)panel.FindName("ReviewDetails")).Text);
            ((PasswordBox)panel.FindName("StagePassword")).Password = newPassword;
            ((Button)panel.FindName("StageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => store.LoadVault().Pending?.Edit.Removals().Contains("vault-added.example") == true &&
                ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
            Assert.Contains(Sites(), site => site.Host == "vault-added.example");
            clock.Advance(TimeSpan.FromSeconds(10));
            panel.Refresh();
            ((PasswordBox)panel.FindName("ConfirmPassword")).Password = newPassword;
            ((Button)panel.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => store.LoadVault().Revision == 2 &&
                ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
            Assert.DoesNotContain(Sites(), site => site.Host == "vault-added.example");
            window.ValidateRetainedTabs();
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);

            Assert.DoesNotContain(Sites(), site => site.Host == "google.com");
            foreach (var host in new[] { "scholar.google.com", "drive.google.com", "mail.google.com" })
            {
                Assert.Contains(Sites(), site => site.Host == host);
                var serviceLoad = WaitForPageAsync(browser.CoreWebView2, $"https://{host}/");
                Request(window, $"https://{host}/");
                await serviceLoad;
                Assert.Equal(Visibility.Visible, browser.Visibility);
            }
            foreach (var host in new[] { "google.com", "www.google.com", "maps.google.com", "child.scholar.google.com" })
            {
                Request(window, $"https://{host}/");
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BoundarySurface")).Visibility);
            }

            ((TextBox)panel.FindName("SiteHost")).Text = "closed-settings.example";
            ((Button)panel.FindName("ReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((PasswordBox)panel.FindName("StagePassword")).Password = newPassword;
            ((Button)panel.FindName("StageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            settings.Close();
            var closingMessage = ((TextBlock)panel.FindName("OutcomeText")).Text;
            await WaitUntilAsync(() => !(bool)typeof(VaultPanel)
                .GetField("_busy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!);
            Assert.Equal("closed-settings.example", Assert.Single(store.LoadVault().Pending!.Edit.Additions()).Host);
            Assert.Equal(closingMessage, ((TextBlock)panel.FindName("OutcomeText")).Text);
            Assert.Empty(((PasswordBox)panel.FindName("StagePassword")).Password);
        }
        finally { settings.Close(); }
    }

    private sealed class AccessTestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan duration) { _now += duration; _timestamp += duration.Ticks; }
    }

    private static ResourceDictionary LoadTheme()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Theme.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            document.Root!.Element(presentation + "Application.Resources")!.Elements());
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }
}

public sealed class WebViewFactAttribute : FactAttribute
{
    public WebViewFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_WEBVIEW_TESTS") != "1")
        {
            Skip = "Set ZENITH_WEBVIEW_TESTS=1 to run with the installed WebView2 runtime and an isolated profile.";
        }
    }
}
