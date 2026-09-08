using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Navigation;
using Zenith.App.Access;
using Zenith.Core.Navigation;
using Zenith.Core.Permissions;

namespace Zenith.App.Tests.Navigation;

internal static class SecurityHardeningScenario
{
    internal static async Task RunAsync()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-SecurityTests-");
        using var vaultStore = new ProtectedAccessStore(Path.Combine(folder.FullName, "Access"));
        vaultStore.Initialize("isolated security fixture password", DateTimeOffset.UtcNow);
        var vaultPath = Path.Combine(folder.FullName, "Access", "access.bin");
        var savedVault = File.ReadAllBytes(vaultPath);
        await using var site = new LoopbackSite("127.0.0.1");
        site.Response = path => path == "/sw.js"
            ? (200, "Content-Type: text/javascript\r\n", "self.addEventListener('install', e => self.skipWaiting()); self.addEventListener('activate', e => e.waitUntil(clients.claim())); self.addEventListener('fetch', e => e.respondWith(new Response('worker response')));")
            : (200, "", "<html><body>Session test</body></html>");
        var policy = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("127.0.0.1", AccessClass.Whitelist)])));
        var window = new MainWindow(new NavigationCoordinator(policy))
            { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var browser = (WebView2)window.FindName("Browser");
        browser.CreationProperties = new() { UserDataFolder = folder.FullName };
        CoreWebView2Environment? environment = null;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            window.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            environment = core.Environment;
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            for (var i = 0; i < 500; i++)
            {
                var tab = typeof(MainWindow).GetField("_activeTab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                if ((bool)tab.GetType().GetProperty("IsReady")!.GetValue(tab)!) break;
                await Task.Delay(20);
            }
            Assert.False(core.Settings.AreHostObjectsAllowed);
            Assert.False(core.Settings.IsWebMessageEnabled);
            Assert.False(core.Settings.IsPasswordAutosaveEnabled);
            Assert.False(core.Settings.IsGeneralAutofillEnabled);
            Assert.False(core.Settings.AreDevToolsEnabled);
            Assert.True(core.Settings.IsReputationCheckingRequired);

            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Geolocation,
                site.Origin, CoreWebView2PermissionState.Allow);
            using (var guard = new BrowserCapabilityGuard(core, new BrowserCapabilityPolicy(), _ => { }))
                await guard.InitializeAsync(false);
            Assert.DoesNotContain(await core.Profile.GetNonDefaultPermissionSettingsAsync(),
                permission => permission.PermissionState == CoreWebView2PermissionState.Allow);

            await CertificateRejectionAsync(window, core);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); };
            Request(window, site.Origin + "/session");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await core.ExecuteScriptAsync("localStorage.setItem('session-test','private'); document.cookie='session=test; path=/';");
            Assert.NotEmpty(await core.CookieManager.GetCookiesAsync(site.Origin));
            await core.ExecuteScriptAsync("navigator.serviceWorker.register('/sw.js').then(() => navigator.serviceWorker.ready).then(() => window.workerReady = true);");
            var workerReady = false;
            for (var i = 0; i < 200; i++)
            {
                if (await core.ExecuteScriptAsync("window.workerReady === true") == "true") { workerReady = true; break; }
                await Task.Delay(20);
            }
            Assert.True(workerReady);
            await core.ExecuteScriptAsync("fetch('/worker-controlled').then(r => r.text()).then(t => window.responseText = t);");
            var bypassed = false;
            for (var i = 0; i < 200; i++)
            {
                if (await core.ExecuteScriptAsync("window.responseText?.includes('Session test') === true") == "true") { bypassed = true; break; }
                await Task.Delay(20);
            }
            Assert.True(bypassed);
            await window.ClearBrowsingDataAndCloseAsync();
            Assert.False(window.IsVisible);
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(savedVault, File.ReadAllBytes(vaultPath));
            Assert.True(vaultStore.Verify("isolated security fixture password"));

            // Reopen the same renderer profile, without touching any real user profile.
            using var probe = new WebView2 { CreationProperties = new() { UserDataFolder = folder.FullName } };
            var probeWindow = new Window { Content = probe, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            var probeExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                probeWindow.Show();
                await probe.EnsureCoreWebView2Async();
                probe.CoreWebView2.Environment.BrowserProcessExited += (_, _) => probeExited.TrySetResult();
                Assert.Empty(await probe.CoreWebView2.CookieManager.GetCookiesAsync(site.Origin));
                var reopened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                probe.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) reopened.TrySetResult(); };
                probe.CoreWebView2.Navigate(site.Origin + "/session");
                await reopened.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("null", await probe.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('session-test')"));
                await probe.CoreWebView2.ExecuteScriptAsync("navigator.serviceWorker.getRegistrations().then(r => window.workerCount = r.length);");
                for (var i = 0; i < 100 && await probe.CoreWebView2.ExecuteScriptAsync("window.workerCount") != "0"; i++)
                    await Task.Delay(20);
                Assert.Equal("0", await probe.CoreWebView2.ExecuteScriptAsync("window.workerCount"));
            }
            finally
            {
                probe.Dispose();
                probeWindow.Close();
                await probeExited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
        finally
        {
            window.Close();
            if (environment is not null) await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            vaultStore.Dispose();
            folder.Delete(true);
        }
    }

    private static async Task CertificateRejectionAsync(MainWindow window, CoreWebView2 core)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Schannel requires a key-container-backed certificate for the server handshake.
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var connection = await listener.AcceptTcpClientAsync(stop.Token);
                    using var tls = new SslStream(connection.GetStream());
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                    await tls.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token);
                }
                catch (Exception ex) when (ex is AuthenticationException or IOException or OperationCanceledException) { }
            }
        });
        var rejected = new TaskCompletionSource<CoreWebView2ServerCertificateErrorAction>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Error(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e) => rejected.TrySetResult(e.Action);
        core.ServerCertificateErrorDetected += Error;
        try
        {
            Request(window, $"https://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");
            Assert.Equal(CoreWebView2ServerCertificateErrorAction.Cancel,
                await rejected.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            core.ServerCertificateErrorDetected -= Error;
            stop.Cancel();
            await server;
        }
    }

    private static void Request(MainWindow window, string target) => typeof(MainWindow)
        .GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [target, NavigationOrigin.AddressBar]);
}
