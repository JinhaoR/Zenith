using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Zenith.App.Access;
using Zenith.App.Registry;
using Zenith.App.Settings;
using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Registry;

public sealed class ServicesUiTests
{
    [Fact]
    public Task OpeningSelectingAndSearchingSettingsServicesCannotChangeVaultOrNavigate() => RunOnSta(RunScenario);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ServiceSelectionRequiresNativeVaultReviewWaitAndConfirmation(bool passwordRequired) =>
        RunOnSta(() => RunServiceScenario(passwordRequired));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task InfrastructureAndLocalExceptionsUseExistingVaultControls(bool password, bool local) =>
        RunOnSta(() => RunRegistryVaultScenario(password, local));

    private static async Task RunRegistryVaultScenario(bool password, bool local)
    {
        const string secret = "native UI test password";
        var folder = Directory.CreateTempSubdirectory("Zenith-InfrastructureUi-");
        SettingsWindow? window = null;
        try
        {
            var clock = new ServiceApprovalPersistenceTests.Clock();
            using var store = new ProtectedAccessStore(folder.FullName);
            if (password) store.Initialize(secret, clock.GetUtcNow()); else store.InitializeWithoutPassword(clock.GetUtcNow());
            store.SaveVault(VaultPermissionLedger.MigrateLegacy(store.LoadVault() with { Sites = local ? [new("mail.google.com", AccessClass.Whitelist, false)] : [] }));
            var vault = new VaultService(store, clock);
            window = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(folder.FullName, "preferences.json")), new(), _ => { }, [],
                _ => throw new InvalidOperationException("Vault reviews must not navigate."), vaultService: vault)
                { Resources = LoadTheme(), ShowActivated = false, Opacity = 0 };
            window.Show();
            window.SelectSection("Vault");
            var editor = (VaultPanel)window.FindName("VaultEditor");
            await editor.LoadRegistryOptionsAsync();
            var before = File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin"));
            if (local)
            {
                var services = (ComboBox)editor.FindName("LocalExtensionService");
                services.SelectedItem = services.Items.Cast<Zenith.Core.Registry.ServiceDefinition>().Single(s => s.Id == "gmail");
                ((TextBox)editor.FindName("LocalExtensionHost")).Text = "login.university.example";
                ((TextBox)editor.FindName("LocalExtensionPurpose")).Text = "Institution sign-in";
                Click("LocalExtensionReviewButton");
            }
            else Click("InfrastructureReviewButton");
            await Layout(window);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin")));
            Assert.True(((FrameworkElement)editor.FindName("ReviewCard")).IsVisible);
            var summary = ((TextBlock)editor.FindName("ReviewSummary")).Text;
            Assert.Contains(local ? "across this profile" : "profile-wide", summary);
            Assert.Contains(local ? "login.university.example" : "accounts.google.com", summary);
            Assert.Equal(password, ((PasswordBox)editor.FindName("StagePassword")).IsVisible);
            if (password) ((PasswordBox)editor.FindName("StagePassword")).Password = secret;
            Click("StageButton");
            await WaitFor(() => store.LoadVault().Pending is not null && ((FrameworkElement)editor.FindName("WorkArea")).IsEnabled);
            Assert.Empty(store.LoadVault().InfrastructureActivations);
            Assert.Empty(store.LoadVault().LocalServiceExtensions);
            Assert.False(((Button)editor.FindName("ConfirmButton")).IsEnabled);
            Assert.False(((Button)editor.FindName("EditButton")).IsEnabled);
            clock.Advance(5);
            editor.Refresh();
            if (password) ((PasswordBox)editor.FindName("ConfirmPassword")).Password = secret;
            Click("ConfirmButton");
            await WaitFor(() => store.LoadVault().Pending is null && ((FrameworkElement)editor.FindName("WorkArea")).IsEnabled);
            if (local) Assert.Single(store.LoadVault().GetActiveLocalExtensions());
            else Assert.Single(store.LoadVault().InfrastructureActivations);
            Assert.Contains(store.LoadVault().Sites, s => s.Host == (local ? "login.university.example" : "accounts.google.com") && !s.IncludeSubdomains);

            void Click(string name) => ((Button)editor.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        finally { window?.Close(); folder.Delete(true); }

        static async Task WaitFor(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Registry Vault UI did not complete."); await Task.Delay(10); }
        }
    }

    private static async Task RunOnSta(Func<Task> scenario)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await scenario(); finished.TrySetResult(); }
                catch (Exception error) { finished.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }

    private static async Task RunServiceScenario(bool passwordRequired)
    {
        const string password = "native UI test password";
        var folder = Directory.CreateTempSubdirectory("Zenith-ServicesVaultUi-");
        SettingsWindow? settings = null;
        try
        {
            var clock = new ServiceApprovalPersistenceTests.Clock();
            using var store = new ProtectedAccessStore(Path.Combine(folder.FullName, "Access"));
            if (passwordRequired) store.Initialize(password, clock.GetUtcNow()); else store.InitializeWithoutPassword(clock.GetUtcNow());
            store.SaveVault(store.LoadVault() with { Sites = [] });
            var vault = new VaultService(store, clock);
            settings = new SettingsWindow(new BrowserPreferencesStore(Path.Combine(folder.FullName, "preferences.json")), new(), _ => { }, [],
                _ => throw new InvalidOperationException("Service proposals must not navigate."), vaultService: vault)
                { Resources = LoadTheme(), ShowActivated = false, Opacity = 0 };
            settings.Show();
            settings.SelectSection("Services");
            var panel = (ServicesPanel)settings.FindName("ServicesBrowser");
            while (panel.Model.IsLoading) await Task.Delay(10);
            var search = (TextBox)panel.FindName("ServiceSearch");
            search.Text = "Gmail";
            await Layout(settings);
            var gmail = Assert.Single(Assert.Single(panel.Model.Categories).Services);
            Descendants<Button>(panel).Single(b => ReferenceEquals(b.Tag, gmail)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(settings);
            Assert.True(panel.Model.CanPrepareSelectedService);
            Click(panel, "Add to Sphere…");
            await Layout(settings);
            Assert.True(panel.Model.CanContinue);
            Assert.Single(panel.Model.Proposal!.ProposedDomains);
            Assert.Empty(store.LoadVault().Sites);
            Assert.Null(store.LoadVault().Pending);
            Click(panel, "Continue to Vault review");
            await Layout(settings);
            var editor = (VaultPanel)settings.FindName("VaultEditor");
            Assert.True(editor.IsVisible);
            Assert.True(((FrameworkElement)editor.FindName("ReviewCard")).IsVisible);
            Assert.Contains("Gmail", ((TextBlock)editor.FindName("ReviewSummary")).Text);
            Assert.DoesNotContain("accounts.google.com", ((TextBlock)editor.FindName("ReviewDetails")).Text);
            Assert.Contains("mail.google.com", ((TextBlock)editor.FindName("ReviewDetails")).Text);
            var stagePassword = (PasswordBox)editor.FindName("StagePassword");
            Assert.Equal(passwordRequired, stagePassword.IsVisible);
            if (passwordRequired) stagePassword.Password = password;
            ((Button)editor.FindName("StageButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => store.LoadVault().Pending is not null && ((FrameworkElement)editor.FindName("WorkArea")).IsEnabled);
            var pending = store.LoadVault().Pending!;
            Assert.NotNull(pending.Edit.ServiceProposal);
            Assert.Empty(store.LoadVault().Sites);
            Assert.Empty(store.LoadVault().ServiceApprovals);
            Assert.False(((Button)editor.FindName("ConfirmButton")).IsEnabled);
            Assert.False(((Button)editor.FindName("EditButton")).IsEnabled);
            clock.Advance(5);
            editor.Refresh();
            Assert.True(((Button)editor.FindName("ConfirmButton")).IsEnabled);
            if (passwordRequired) ((PasswordBox)editor.FindName("ConfirmPassword")).Password = password;
            ((Button)editor.FindName("ConfirmButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => store.LoadVault().Pending is null && ((FrameworkElement)editor.FindName("WorkArea")).IsEnabled);
            Assert.Single(store.LoadVault().Sites);
            Assert.Single(store.LoadVault().ServiceApprovals);
            Assert.Equal(pending.Id, store.LoadVault().ServiceApprovals[0].VaultProposalId);
        }
        finally { settings?.Close(); folder.Delete(true); }

        static void Click(DependencyObject parent, string label) => Descendants<Button>(parent)
            .Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static async Task WaitFor(Func<bool> done)
        {
            var limit = DateTime.UtcNow.AddSeconds(10);
            while (!done()) { if (DateTime.UtcNow >= limit) throw new TimeoutException("Vault UI transition did not complete."); await Task.Delay(10); }
        }
    }

    private static async Task RunScenario()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-ServicesUi-");
        SettingsWindow? settings = null;
        try
        {
            using var store = new ProtectedAccessStore(Path.Combine(folder.FullName, "Access"));
            var clock = new FixedClock();
            store.InitializeWithoutPassword(clock.GetUtcNow());
            store.SaveVault(VaultPermissionLedger.MigrateLegacy(store.LoadVault() with { Sites = [
                new("github.com", AccessClass.Whitelist, false),
                new("mail.google.com", AccessClass.Blacklist, false)] }));
            var vault = new VaultService(store, clock);
            var evaluator = new SitePolicyNavigationEvaluator(vault);
            var navigations = 0;
            var policyChanges = 0;
            var preferencesPath = Path.Combine(folder.FullName, "preferences.json");
            settings = new SettingsWindow(new BrowserPreferencesStore(preferencesPath), new(), _ => throw new InvalidOperationException("No preference edits expected."),
                [new("GitHub", "github.com", "https://github.com")], _ => navigations++, vaultService: vault,
                policyChanged: () => policyChanges++) { Resources = LoadTheme(), ShowActivated = false, Opacity = 0 };
            settings.Show();
            await Layout(settings);
            var panel = (ServicesPanel)settings.FindName("ServicesBrowser");
            Assert.False(panel.Model.IsAvailable);
            var vaultPath = Path.Combine(folder.FullName, "Access", "access.bin");
            var before = File.ReadAllBytes(vaultPath);
            settings.SelectSection("Services");
            while (panel.Model.IsLoading) await Task.Delay(10);
            await Layout(settings);
            Assert.True(panel.Model.IsAvailable);
            Assert.True(panel.IsVisible);
            Assert.Equal(Visibility.Collapsed, ((ContentControl)panel.FindName("Details")).Visibility);
            Assert.Equal(before, File.ReadAllBytes(vaultPath));
            Assert.Equal("Services", ((TextBlock)settings.FindName("PageTitle")).Text);
            Assert.Equal("Viewing this catalog does not change permissions.", ((TextBlock)settings.FindName("SaveStatus")).Text);

            var search = (TextBox)panel.FindName("ServiceSearch");
            search.Text = "GitHub";
            await Layout(settings);
            var github = Assert.Single(Assert.Single(panel.Model.Categories).Services);
            Assert.Equal("Available in Registry", github.MembershipLabel);
            var button = Descendants<Button>(panel).Single(b => ReferenceEquals(b.Tag, github));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(settings);
            Assert.Same(github, panel.Model.SelectedService);
            Assert.Equal(Visibility.Visible, ((ContentControl)panel.FindName("Details")).Visibility);
            Assert.Contains(Descendants<TextBlock>(panel), text => text.Text == "Available in Registry");
            Assert.Equal(before, File.ReadAllBytes(vaultPath));

            search.Text = "Gmail";
            await Layout(settings);
            var gmail = Assert.Single(Assert.Single(panel.Model.Categories).Services);
            Descendants<Button>(panel).Single(b => ReferenceEquals(b.Tag, gmail)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(settings);
            foreach (var expander in Descendants<Expander>(panel).ToArray()) expander.IsExpanded = true;
            await Layout(settings);
            Assert.Contains(Descendants<TextBlock>(panel), text => text.Text == "mail.google.com");
            Assert.Contains(Descendants<TextBlock>(panel), text => text.Text == "accounts.google.com");
            Assert.Contains(Descendants<TextBlock>(panel), text => text.Text == "Required: no (optional)");
            Assert.Contains(Descendants<TextBlock>(panel), text => text.Text == "Dependency classification: authentication");
            Assert.Equal(before, File.ReadAllBytes(vaultPath));

            Assert.IsType<NavigationDecision.Allowed>(Decide("github.com"));
            Assert.Equal(NavigationDenialReason.Blacklisted, Assert.IsType<NavigationDecision.Denied>(Decide("mail.google.com")).Reason);
            Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(Decide("mail.proton.me")).Reason);
            search.Text = "https://unlisted.example/";
            await Layout(settings);
            Assert.Empty(panel.Model.Categories);
            Assert.Null(panel.Model.SelectedService);
            Assert.Equal(Visibility.Collapsed, ((ContentControl)panel.FindName("Details")).Visibility);
            Assert.Equal(before, File.ReadAllBytes(vaultPath));
            Assert.Equal(0, navigations);
            Assert.Equal(0, policyChanges);
            Assert.False(File.Exists(preferencesPath));

            NavigationDecision Decide(string host) => evaluator.Evaluate(new($"https://{host}", NavigationOrigin.AddressBar));
        }
        finally
        {
            settings?.Close();
            folder.Delete(true);
        }
    }

    private static async Task Layout(FrameworkElement element)
    {
        await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
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

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        public override long GetTimestamp() => 0;
    }
}
