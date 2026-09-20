using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Extensions;
using Zenith.App.Settings;

namespace Zenith.App.Tests.Settings;

internal static class ContentProtectionScenario
{
    internal static async Task RunAsync()
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-ExtensionStatus-");
        var browser = new WebView2();
        var host = new Window { Content = browser, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        SettingsWindow? settings = null;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ContentProtectionStatus>();
        var initialized = false;
        try
        {
            // A metadata-only fixture, not uBO Lite or a filtering implementation.
            var extensionDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "fixture"));
            File.WriteAllText(Path.Combine(extensionDirectory.FullName, "manifest.json"),
                """{"manifest_version":3,"name":"uBlock Origin Lite","version":"1.2.3"}""");
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(directory.FullName, "Browser"),
                new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true });
            host.Show();
            await browser.EnsureCoreWebView2Async(environment);
            environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            initialized = true;
            var profile = browser.CoreWebView2.Profile;
            Func<Task<ContentProtectionStatus>> read = () => ContentProtectionReader.ReadAsync(profile);
            settings = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(directory.FullName, "preferences.json")),
                new(), _ => { }, [], _ => throw new InvalidOperationException("Status must not navigate."),
                contentProtectionStatus: () => read()) { Owner = host, Opacity = 0, ShowActivated = false };
            settings.Show();
            settings.SelectSection("ContentProtection");
            await Expect("Not installed in the current browser profile");
            var extension = await profile.AddBrowserExtensionAsync(extensionDirectory.FullName);
            Refresh();
            await Expect("Enabled");
            await extension.EnableAsync(false);
            Refresh();
            await Expect("Disabled");
            await extension.EnableAsync(true);
            Refresh();
            await Expect("Enabled");
            await extension.RemoveAsync();
            Refresh();
            await Expect("Not installed in the current browser profile");
            read = () => throw new InvalidOperationException("Simulated controller failure");
            Refresh();
            await Expect("Unavailable — browser extension status could not be read");
            read = () => pending.Task;
            Refresh();
            Assert.Equal("Checking…", ((TextBlock)settings.FindName("ProtectionStatusText")).Text);
            settings.Close();
            pending.SetResult(ContentProtectionStatus.Enabled);
            await settings.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Assert.Equal("Checking…", ((TextBlock)settings.FindName("ProtectionStatusText")).Text);
            Console.WriteLine("Content Protection: native MV3 metadata enumeration + Settings enabled/disabled/removed/error/closed-during-refresh passed; fixture does not test filtering");

            void Refresh() => ((Button)settings.FindName("RefreshProtectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task Expect(string text)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (((TextBlock)settings.FindName("ProtectionStatusText")).Text != text ||
                    !((Button)settings.FindName("RefreshProtectionButton")).IsEnabled)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Extension status did not become " + text);
                    await Task.Delay(20);
                }
            }
        }
        finally
        {
            settings?.Close();
            browser.Dispose();
            host.Close();
            if (initialized)
                await exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            try { directory.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
