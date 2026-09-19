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
using System.Text.Json;
using Zenith.Core.Filtering;
using Zenith.App.Filtering;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            ? (200, "Content-Type: text/javascript\r\n", "self.addEventListener('install', e => self.skipWaiting()); self.addEventListener('activate', e => e.waitUntil(clients.claim())); self.addEventListener('fetch', e => { if(!e.request.url.includes('/denied-')) e.respondWith(new Response('worker response')); }); self.onmessage=e=>e.waitUntil(fetch(e.data).then(r=>e.ports[0].postMessage(r.status)).catch(()=>e.ports[0].postMessage('failed')));")
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
            await SeedWorkerAsync(folder.FullName, site.Origin);
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
            Assert.False(browser.AllowExternalDrop);
            Assert.False(core.Settings.IsWebMessageEnabled);
            Assert.False(core.Settings.IsPasswordAutosaveEnabled);
            Assert.False(core.Settings.IsGeneralAutofillEnabled);
            Assert.False(core.Settings.AreDevToolsEnabled);
            Assert.True(core.Settings.IsReputationCheckingRequired);
            await FaviconBytesAsync();

            await core.Profile.SetPermissionStateAsync(CoreWebView2PermissionKind.Geolocation,
                site.Origin, CoreWebView2PermissionState.Allow);
            using (var guard = new BrowserCapabilityGuard(core, new BrowserCapabilityPolicy(), _ => { }, () => browser.Dispose()))
                await guard.InitializeAsync(false);
            Assert.DoesNotContain(await core.Profile.GetNonDefaultPermissionSettingsAsync(),
                permission => permission.PermissionState == CoreWebView2PermissionState.Allow);

            await CertificateRejectionAsync(window, core);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); };
            Request(window, site.Origin + "/session");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await core.ExecuteScriptAsync("document.title='Trusted Primary Email Login';");
            Assert.Equal(site.Origin + " — Zenith", window.Title);
            await FilePickerCancellationAsync(core);
            await ResourceFailureAsync(core, site);
            Assert.Equal("\"preserved\"", await core.ExecuteScriptAsync("localStorage.getItem('worker-cleanup-control')"));
            Assert.Contains(await core.CookieManager.GetCookiesAsync(site.Origin), c => c.Name == "worker-cleanup-control");
            await core.ExecuteScriptAsync("localStorage.setItem('session-test','private'); document.cookie='session=test; path=/';");
            Assert.NotEmpty(await core.CookieManager.GetCookiesAsync(site.Origin));
            await core.ExecuteScriptAsync("navigator.serviceWorker.getRegistrations().then(r => window.legacyWorkers = r.length);");
            for (var i = 0; i < 200 && await core.ExecuteScriptAsync("window.legacyWorkers") != "0"; i++)
                await Task.Delay(20);
            Assert.Equal("0", await core.ExecuteScriptAsync("window.legacyWorkers"));
            await core.ExecuteScriptAsync("navigator.serviceWorker.register('/denied-new-sw.js').catch(() => window.registrationDenied=true);");
            for (var i = 0; i < 200 && await core.ExecuteScriptAsync("window.registrationDenied") != "true"; i++)
                await Task.Delay(20);
            Assert.Equal("true", await core.ExecuteScriptAsync("window.registrationDenied"));
            Assert.DoesNotContain("/denied-new-sw.js", site.Requests);
            await core.ExecuteScriptAsync("fetch('/worker-controlled').then(r => r.text()).then(t => window.responseText = t);");
            var bypassed = false;
            for (var i = 0; i < 200; i++)
            {
                if (await core.ExecuteScriptAsync("window.responseText?.includes('Session test') === true") == "true") { bypassed = true; break; }
                await Task.Delay(20);
            }
            Assert.True(bypassed);
            await WorkerNetworkDenialAsync(core, site);
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

    private static async Task SeedWorkerAsync(string profile, string origin)
    {
        // Simulate an old profile: seed using an unprotected test-only controller,
        // then fully exit that browser process before Zenith installs its guards.
        using var browser = new WebView2 { CreationProperties = new() { UserDataFolder = profile } };
        var host = new Window { Content = browser, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialized = false;
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async();
            initialized = true;
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); };
            core.Navigate(origin + "/seed");
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await core.ExecuteScriptAsync("navigator.serviceWorker.register('/sw.js').then(()=>navigator.serviceWorker.ready).then(()=>window.seeded=true);");
            for (var i = 0; i < 200 && await core.ExecuteScriptAsync("window.seeded") != "true"; i++)
                await Task.Delay(20);
            Assert.Equal("true", await core.ExecuteScriptAsync("window.seeded"));
            await core.ExecuteScriptAsync("localStorage.setItem('worker-cleanup-control','preserved'); document.cookie='worker-cleanup-control=preserved; path=/; max-age=86400';");
        }
        finally
        {
            browser.Dispose();
            host.Close();
            if (initialized) await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task FilePickerCancellationAsync(CoreWebView2 core)
    {
        foreach (var nested in new[] { false, true })
        {
            await core.ExecuteScriptAsync(nested
                ? "window.pickerFrame=document.createElement('iframe'); document.body.appendChild(pickerFrame); window.pickerDoc=pickerFrame.contentDocument;"
                : "window.pickerDoc=document;");
            await core.ExecuteScriptAsync("window.fileInput=pickerDoc.createElement('input'); fileInput.type='file'; pickerDoc.body.appendChild(fileInput); window.pickerCancelled=false; fileInput.oncancel=()=>window.pickerCancelled=true;");
            await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = "fileInput.click()", userGesture = true
            }));
            for (var i = 0; i < 100 && await core.ExecuteScriptAsync("window.pickerCancelled") != "true"; i++)
                await Task.Delay(20);
            Assert.Equal("true", await core.ExecuteScriptAsync("window.pickerCancelled"));
            Assert.Equal("0", await core.ExecuteScriptAsync("fileInput.files.length"));
        }
    }

    private static async Task FaviconBytesAsync()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32,
            null, new byte[] { 128, 128, 128, 255 }, 4)));
        using var data = new MemoryStream();
        encoder.Save(data);
        data.Position = 0;
        var icon = await FaviconDecoder.DecodeAsync(data);
        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
    }

    private static async Task WorkerNetworkDenialAsync(CoreWebView2 core, LoopbackSite site)
    {
        await core.ExecuteScriptAsync("""
            const script = `onconnect=e=>{ const p=e.ports[0]; p.onmessage=m=>fetch(m.data).then(r=>p.postMessage(r.status)).catch(()=>p.postMessage('failed')); p.start(); };`;
            window.sharedProbe=new SharedWorker(URL.createObjectURL(new Blob([script], {type:'text/javascript'})));
            window.sharedStatus=null;
            sharedProbe.port.onmessage=e=>window.sharedStatus=e.data;
            sharedProbe.port.start();
            sharedProbe.port.postMessage(location.origin+'/denied-shared-worker');
            """);
        for (var i = 0; i < 250; i++)
        {
            if (await core.ExecuteScriptAsync("sharedStatus !== null") == "true") break;
            await Task.Delay(20);
        }
        Assert.Equal("403", await core.ExecuteScriptAsync("sharedStatus"));
        Assert.DoesNotContain("/denied-shared-worker", site.Requests);
        await core.ExecuteScriptAsync("sharedProbe.port.close();");
    }

    private sealed class BrokenFilter : IResourceFilterEngine
    {
        public bool Blocks(ResourceRequest request) => throw new InvalidOperationException("Injected filter failure");
    }
    private sealed class Source : IBlacklistSource
    {
        public HostsBlacklist Current { get; } = HostsBlacklist.Parse("0.0.0.0 blocked.example");
    }

    private static async Task ResourceFailureAsync(CoreWebView2 core, LoopbackSite site)
    {
        using var guard = new ResourceRequestGuard(core, new Source(), new BrokenFilter(),
            () => Assert.Fail("Native response handling failed"));
        await core.ExecuteScriptAsync("fetch('/must-not-reach-server').then(r=>window.deniedStatus=r.status);");
        for (var i = 0; i < 100 && await core.ExecuteScriptAsync("window.deniedStatus") != "403"; i++)
            await Task.Delay(20);
        Assert.Equal("403", await core.ExecuteScriptAsync("window.deniedStatus"));
        Assert.DoesNotContain("/must-not-reach-server", site.Requests);
    }

    private static void Request(MainWindow window, string target) => typeof(MainWindow)
        .GetMethod("RequestNavigation", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(window, [target, NavigationOrigin.AddressBar]);
}
