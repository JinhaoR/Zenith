using System.Windows;
using System.Windows.Controls;
using Zenith.App.Access;
using Zenith.App.Settings;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Settings;

internal static class VaultEditingScenario
{
    private const string Password = "native Vault regression password";

    internal static async Task RunAsync()
    {
        await ExerciseAsync(false);
        await ExerciseAsync(true);
    }

    private static async Task ExerciseAsync(bool passwordRequired)
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-VaultEditing-");
        var clock = new Clock();
        using var store = new ProtectedAccessStore(directory.FullName);
        if (passwordRequired) store.Initialize(Password, clock.Now);
        else store.InitializeWithoutPassword(clock.Now);
        store.SaveVault(store.LoadVault() with { Sites = [] });
        var service = new VaultService(store, clock);
        var panel = new VaultPanel();
        var host = new Window { Content = panel, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        panel.Configure(service, () => throw new InvalidOperationException("Unexpected password setup"), () => { });
        try
        {
            host.Show();
            panel.Refresh();
            var search = (TextBox)panel.FindName("KnownSiteFilter");
            var queued = (ItemsControl)panel.FindName("DraftAdditions");
            search.Text = "Gmail";
            Click("AddSiteButton");
            Assert.Empty(queued.Items); // A friendly-name search alone is not a hostname.
            ((ComboBox)panel.FindName("KnownSite")).SelectedIndex = 0;
            Assert.Equal("mail.google.com", ((TextBox)panel.FindName("SiteHost")).Text);
            search.Text = "no matching service";
            Assert.Empty(((TextBox)panel.FindName("SiteHost")).Text);
            foreach (var hostname in new[] { "mail.google.com", "account.google.com" })
            {
                search.Text = hostname;
                if (hostname == "mail.google.com") Assert.Single(((ComboBox)panel.FindName("KnownSite")).Items.Cast<object>());
                Click("AddSiteButton");
            }
            panel.Refresh();
            Assert.Equal(2, queued.Items.Count);
            Click("ReviewButton");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("ReviewCard")).Visibility);
            foreach (var hostname in new[] { "mail.google.com", "account.google.com" })
                Assert.Contains(hostname, ((TextBlock)panel.FindName("ReviewDetails")).Text);
            Assert.Equal(passwordRequired ? Visibility.Visible : Visibility.Collapsed,
                ((FrameworkElement)panel.FindName("StagePassword")).Visibility);
            if (passwordRequired)
            {
                Click("StageButton");
                await Idle();
                Assert.Null(store.LoadVault().Pending);
                Assert.Contains("password did not match", ((TextBlock)panel.FindName("OutcomeText")).Text);
                clock.Advance(5);
                Click("ReviewButton");
                ((PasswordBox)panel.FindName("StagePassword")).Password = Password;
            }
            Click("StageButton");
            await Idle();
            var pending = Assert.IsType<PendingPolicyChange>(store.LoadVault().Pending);
            Assert.Equal(2, pending.Edit.Additions().Count());
            Assert.Empty(store.LoadVault().Sites);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("PendingCard")).Visibility);
            Assert.False(((Button)panel.FindName("ConfirmButton")).IsEnabled);
            Assert.Equal(VaultResult.TooEarly, service.Confirm(pending.Id, passwordRequired ? Password : "").Result);
            clock.Advance(5);
            panel.Refresh();
            Assert.Equal(passwordRequired ? Visibility.Visible : Visibility.Collapsed,
                ((FrameworkElement)panel.FindName("ConfirmPassword")).Visibility);
            if (passwordRequired) ((PasswordBox)panel.FindName("ConfirmPassword")).Password = Password;
            Click("ConfirmButton");
            await Idle();
            Assert.Null(store.LoadVault().Pending);
            Assert.Equal(new[] { "account.google.com", "mail.google.com" }, store.LoadVault().Sites.Select(site => site.Host).Order().ToArray());
            search.Text = "https://invalid.example/path";
            Click("AddSiteButton");
            Assert.Empty(queued.Items);
            Assert.NotEmpty(((TextBlock)panel.FindName("OutcomeText")).Text);
            Assert.Null(store.LoadVault().Pending);
            search.Clear();
            var removalSearch = (TextBox)panel.FindName("RemoveSiteFilter");
            var removals = (ListBox)panel.FindName("RemoveSite");
            removalSearch.Text = "GmAiL";
            Assert.Single(removals.Items.Cast<object>());
            removals.SelectedItems.Add(removals.Items[0]);
            removalSearch.Text = "ACCOUNT.GOOGLE.COM";
            Assert.Single(removals.Items.Cast<object>());
            removals.SelectedItems.Add(removals.Items[0]);
            Assert.Contains("2 selected", ((TextBlock)panel.FindName("RemovalSummary")).Text);
            removalSearch.Text = "no matching site";
            Assert.Empty(removals.Items);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)panel.FindName("NoRemovalMatches")).Visibility);
            Click("ReviewButton");
            foreach (var hostname in new[] { "mail.google.com", "account.google.com" })
                Assert.Contains("Remove: " + hostname, ((TextBlock)panel.FindName("ReviewDetails")).Text);
            removalSearch.Clear();
            Assert.Equal(2, removals.SelectedItems.Count);
            Assert.True(((Button)panel.FindName("StageButton")).IsEnabled); // Search is not a draft edit.
            removalSearch.Text = "no matching site";
            Click("ClearRemovalButton");
            Assert.False(((Button)panel.FindName("StageButton")).IsEnabled);
            removalSearch.Clear();
            Assert.Empty(removals.SelectedItems);
            removals.SelectAll();
            Click("ReviewButton");
            if (passwordRequired) ((PasswordBox)panel.FindName("StagePassword")).Password = Password;
            Click("StageButton");
            await Idle();
            Assert.Equal(2, store.LoadVault().Pending!.Edit.Removals().Count());
            Assert.Equal(2, store.LoadVault().Sites.Length);
            // Editing a pending proposal restores selections, even after filtering.
            Click("EditButton");
            Assert.Equal(2, removals.SelectedItems.Count);
            removalSearch.Text = "Gmail";
            Assert.Single(removals.SelectedItems.Cast<object>());
            Click("ReviewButton");
            if (passwordRequired) ((PasswordBox)panel.FindName("StagePassword")).Password = Password;
            Click("StageButton");
            await Idle();
            clock.Advance(5);
            panel.Refresh();
            if (passwordRequired) ((PasswordBox)panel.FindName("ConfirmPassword")).Password = Password;
            Click("ConfirmButton");
            await Idle();
            Assert.Empty(store.LoadVault().Sites);
            Assert.Null(store.LoadVault().Pending);
            Console.WriteLine($"Vault editing: hostname search/add, two-entry preview, staging, wait, confirmation and invalid input passed (password={passwordRequired})");
            Console.WriteLine($"Vault removal search: names/hosts, hidden selections, clear, edit pending and confirmed batch removal passed (password={passwordRequired})");

            void Click(string name) => ((Button)panel.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            async Task Idle()
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!((FrameworkElement)panel.FindName("WorkArea")).IsEnabled)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException("Vault operation did not finish.");
                    await Task.Delay(20);
                }
            }
        }
        finally
        {
            host.Close();
            store.Dispose();
            directory.Delete(true);
        }
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
