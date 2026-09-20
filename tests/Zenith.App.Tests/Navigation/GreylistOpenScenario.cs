using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Access;
using Zenith.App.Navigation;
using Zenith.App.Settings;
using Zenith.Core.Access;
using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Navigation;

internal static class GreylistOpenScenario
{
    internal static async Task RunAsync()
    {
        await using var origin = new LoopbackSite("127.0.0.1");
        await using var denied = new LoopbackSite("127.0.0.2");
        origin.Response = path => path.StartsWith("/redirect")
            ? (302, $"Location: {denied.Origin}/denied\r\n", "")
            : (200, "", "<html><body>Permitted temporary visit</body></html>");
        var profile = Directory.CreateTempSubdirectory("Zenith-GreylistOpen-");
        var clock = new Clock();
        using var store = new ProtectedAccessStore(Path.Combine(profile.FullName, "Access"));
        store.InitializeWithoutPassword(clock.Now);
        var policy = new VaultService(store, clock);
        var access = new GreylistAccessService(policy, store, store, clock);
        var coordinator = new NavigationCoordinator(new SitePolicyNavigationEvaluator(policy, access, clock));
        var host = new MainWindow(coordinator, access, policy) { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        var browser = (WebView2)host.FindName("Browser");
        browser.CreationProperties = new() { UserDataFolder = Path.Combine(profile.FullName, "Browser"), AdditionalBrowserArguments = "--no-proxy-server" };
        var closed = false;
        host.Closed += (_, _) => closed = true;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            await Until(() => (bool)(Field(host, "_activeTab")?.GetType().GetProperty("IsReady")?.GetValue(Field(host, "_activeTab")) ?? false));
            await CheckWwwRedirectAsync(false);
            var target = new Uri(origin.Origin + "/redirect");
            Invoke(host, "RequestNavigation", target.AbsoluteUri, NavigationOrigin.AddressBar);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)host.FindName("BoundarySurface")).Visibility);
            Invoke(host, "OpenTemporaryAccess", target);
            var gate = (AccessWindow)Field(host, "_accessWindow")!;
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)gate.FindName("PasswordPanel")).Visibility);
            Click(gate, "SubmitButton");
            await Until(() => access.GetStatus(target.AbsoluteUri).Phase == AccessPhase.Cooldown);
            Assert.DoesNotContain("/redirect", origin.Requests);
            gate.Close();
            clock.Advance(5);
            Assert.Empty(access.GetActiveGrants());
            Invoke(host, "OpenTemporaryAccess", target);
            gate = (AccessWindow)Field(host, "_accessWindow")!;
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)gate.FindName("PasswordPanel")).Visibility);
            Click(gate, "SubmitButton");
            await Until(() => access.GetStatus(target.AbsoluteUri).Phase == AccessPhase.Granted);
            await WaitForBoundary();

            // Unrelated destinations must still be denied after a temporary grant.
            // Native cancellation + host blank navigation can invalidate a Fetch
            // request while its event or command is still queued on the dispatcher.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                Invoke(host, "RequestNavigation", origin.Origin + "/redirect?attempt=" + attempt, NavigationOrigin.AddressBar);
                await Until(() => origin.Requests.Contains("/redirect?attempt=" + attempt));
                await WaitForBoundary();
            }
            Assert.False(closed);
            Assert.DoesNotContain("/denied", denied.Requests);
            Invoke(host, "RequestNavigation", origin.Origin + "/allowed", NavigationOrigin.AddressBar);
            await Until(() => !closed && core.Source == origin.Origin + "/allowed");
            Assert.False(closed);
            clock.Advance(5);
            host.ValidateRetainedTabs();
            await Until(() => !closed && core.Source == "about:blank");
            Assert.False(closed);

            // Settings can opt in; changing that choice retains the Vault wait.
            var panel = new VaultPanel();
            var settings = new Window { Content = panel, Owner = host, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            panel.Configure(policy, () => throw new InvalidOperationException("No setup gate should be needed."), () => { });
            try
            {
                settings.Show();
                panel.Refresh();
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)panel.FindName("StagePassword")).Visibility);
                ((CheckBox)panel.FindName("ChangePassword")).IsChecked = true;
                ((PasswordBox)panel.FindName("NewPassword")).Password = "optional native test password";
                ((PasswordBox)panel.FindName("RepeatPassword")).Password = "optional native test password";
                Click(panel, "ReviewButton");
                Click(panel, "StageButton");
                await Until(() => store.LoadVault().Pending is not null && ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
                Assert.False(store.PasswordRequired);
                clock.Advance(5);
                panel.Refresh();
                Click(panel, "ConfirmButton");
                await Until(() => store.PasswordRequired && ((FrameworkElement)panel.FindName("WorkArea")).IsEnabled);
                Assert.True(store.Verify("optional native test password"));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("StagePassword")).Visibility);
            }
            finally { settings.Close(); }
            await CheckWwwRedirectAsync(true);
            Console.WriteLine("Greylist open: cooldown-only UI, 21 denied redirects, retained controller, expiry and optional password settings passed");

            async Task CheckWwwRedirectAsync(bool passwordRequired)
            {
                var bare = passwordRequired ? "confirmed-greylist.test" : "requested-greylist.test";
                var requested = new Uri($"https://{bare}/watch?v=preserved");
                var destination = new Uri($"https://www.{bare}/watch?v=preserved");
                var observed = new List<string>();
                var streams = new List<MemoryStream>();
                var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                core.AddWebResourceRequestedFilter("https://*" + bare + "/*", CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
                core.WebResourceRequested += Respond;
                core.NavigationCompleted += Completed;
                try
                {
                    // Use the real Settings address parser and native confirmation window.
                    Invoke(host, "OpenSettings", "Access");
                    var accessSettings = (SettingsWindow)Field(host, "_settingsWindow")!;
                    ((TextBox)accessSettings.FindName("AccessAddress")).Text = bare + "/watch?v=preserved";
                    Click(accessSettings, "BeginAccessButton");
                    var confirmation = (AccessWindow)Field(host, "_accessWindow")!;
                    SetPassword(confirmation);
                    Click(confirmation, "SubmitButton");
                    await Until(() => access.GetStatus(requested.AbsoluteUri).Phase == AccessPhase.Cooldown &&
                        ((Button)confirmation.FindName("SubmitButton")).Visibility == Visibility.Collapsed);
                    Assert.Equal(requested.AbsoluteUri, Assert.Single(access.GetPendingRequests()).Target);
                    Assert.Empty(observed);
                    confirmation.Close();
                    clock.Advance(5);
                    Assert.Equal(AccessPhase.SecondChallenge, access.GetStatus(requested.AbsoluteUri).Phase);
                    Assert.NotEqual(AccessPhase.Granted, access.GetStatus(destination.AbsoluteUri).Phase);
                    Invoke(host, "OpenTemporaryAccess", requested);
                    confirmation = (AccessWindow)Field(host, "_accessWindow")!;
                    SetPassword(confirmation);
                    Click(confirmation, "SubmitButton");
                    await Until(() => !confirmation.IsVisible && core.Source == destination.AbsoluteUri);
                    await Until(() => observed.Contains(destination.AbsoluteUri));
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.Equal(AccessPhase.Granted, access.GetStatus(destination.AbsoluteUri).Phase);
                    Assert.Equal(access.GetStatus(requested.AbsoluteUri).ExpiresAt, access.GetStatus(destination.AbsoluteUri).ExpiresAt);
                    Assert.Empty(access.GetPendingRequests());
                    Assert.Contains(requested.AbsoluteUri, observed);
                    Assert.Equal("\"Temporary visit loaded\"", await core.ExecuteScriptAsync("document.body.textContent"));
                    clock.Advance(5);
                    host.ValidateRetainedTabs();
                    await Until(() => core.Source == "about:blank");
                    Assert.NotEqual(AccessPhase.Granted, access.GetStatus(destination.AbsoluteUri).Phase);
                    Assert.False(closed);
                    Console.WriteLine($"Greylist www redirect: Settings address without www, full URI, both confirmations, native document and expiry passed (password={passwordRequired})");
                }
                finally
                {
                    core.WebResourceRequested -= Respond;
                    core.NavigationCompleted -= Completed;
                    foreach (var stream in streams) stream.Dispose();
                }

                void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.IsSuccess && core.Source == destination.AbsoluteUri) loaded.TrySetResult();
                }

                void SetPassword(AccessWindow confirmation)
                {
                    Assert.Equal(passwordRequired ? Visibility.Visible : Visibility.Collapsed,
                        ((FrameworkElement)confirmation.FindName("PasswordPanel")).Visibility);
                    if (passwordRequired) ((PasswordBox)confirmation.FindName("Password")).Password = "optional native test password";
                }

                void Respond(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    var uri = new Uri(e.Request.Uri);
                    if (uri.Host != bare && uri.Host != "www." + bare) return;
                    observed.Add(e.Request.Uri);
                    var redirect = uri.Host == bare;
                    var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(redirect ? "" : "<html><body>Temporary visit loaded</body></html>"));
                    streams.Add(stream);
                    e.Response = core.Environment.CreateWebResourceResponse(stream, redirect ? 302 : 200,
                        redirect ? "Found" : "OK", redirect ? $"Location: {destination.AbsoluteUri}\r\n" : "Content-Type: text/html\r\n");
                }
            }

            async Task WaitForBoundary()
            {
                await Until(() => closed || ((TextBlock)host.FindName("BoundaryTargetTextBlock")).Text == denied.Origin + "/denied" && core.Source == "about:blank");
                Assert.False(closed);
                await Task.Delay(100); // Let late native/CDP callbacks run after blank completion.
                Assert.False(closed);
            }
        }
        finally
        {
            if (!closed) host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            store.Dispose();
            try { profile.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value);
    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args);
    private static void Click(FrameworkElement root, string name) => ((Button)root.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> ready)
    {
        var stop = DateTime.UtcNow.AddSeconds(15);
        while (!ready()) { if (DateTime.UtcNow > stop) throw new TimeoutException("Greylist open scenario did not reach the expected state."); await Task.Delay(20); }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => Now;
        internal void Advance(int seconds) { Now = Now.AddSeconds(seconds); _ticks += TimeSpan.FromSeconds(seconds).Ticks; }
    }
}
