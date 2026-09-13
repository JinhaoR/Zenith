using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Zenith.App.Settings;

namespace Zenith.App.Tests.Settings;

internal static class SettingsInteractionScenario
{
    internal static async Task RunAsync()
    {
        var combo = new ComboBox { ItemsSource = Enumerable.Range(0, 100).ToArray(), SelectedIndex = 0 };
        var nested = new ScrollViewer { Height = 120, Content = new Border { Height = 800 }, CanContentScroll = false };
        var content = new StackPanel();
        content.Children.Add(combo);
        content.Children.Add(nested);
        content.Children.Add(new Border { Height = 1500 });
        var outer = new ScrollViewer { Content = content, CanContentScroll = false };
        var host = new Window { Content = outer, Width = 500, Height = 450, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zenith.App;component/Settings/SettingsControls.xaml", UriKind.Relative) });
        host.PreviewMouseWheel += SettingsScrollBehavior.HandleWheel;
        try
        {
            host.Show();
            host.UpdateLayout();
            Assert.Equal(300, combo.MaxDropDownHeight);
            Wheel(combo, -120);
            await LayoutAsync(host);
            Assert.Equal(0, combo.SelectedIndex);
            Assert.True(outer.VerticalOffset > 0);
            outer.ScrollToTop();
            await LayoutAsync(host);
            combo.IsDropDownOpen = true;
            await LayoutAsync(host);
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            var dropdown = Assert.IsType<ScrollViewer>(((Border)popup.Child).Child);
            Assert.InRange(dropdown.ActualHeight, 1, 300);
            Assert.True(dropdown.ScrollableHeight > 0);
            combo.IsDropDownOpen = false;

            outer.ScrollToTop();
            await LayoutAsync(host);
            Wheel(nested, -120);
            await LayoutAsync(host);
            Assert.True(nested.VerticalOffset > 0);
            Assert.Equal(0, outer.VerticalOffset);

            nested.ScrollToBottom();
            await LayoutAsync(host);
            Wheel(nested, -120);
            await LayoutAsync(host);
            Assert.True(outer.VerticalOffset > 0);

            outer.ScrollToBottom();
            await LayoutAsync(host);
            Wheel(combo, -120);
            await LayoutAsync(host);
            Assert.Equal(0, combo.SelectedIndex);
        }
        finally { host.Close(); }
        await CheckInputTemplatesAsync();
    }

    private static async Task CheckInputTemplatesAsync()
    {
        var text = new TextBox { Width = 180, Text = new string('w', 160) };
        var password = new PasswordBox { Width = 180 };
        var check = new CheckBox { Content = "Include subdomains", IsThreeState = true };
        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(password);
        panel.Children.Add(check);
        var host = new Window { Content = panel, Width = 380, Height = 300, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        host.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Zenith.App;component/Settings/SettingsControls.xaml", UriKind.Relative) });
        try
        {
            host.Show();
            await LayoutAsync(host);
            Assert.IsType<ScrollViewer>(text.Template.FindName("PART_ContentHost", text));
            Assert.IsType<ScrollViewer>(password.Template.FindName("PART_ContentHost", password));
            Assert.Equal(128, password.MaxLength);
            text.ScrollToHorizontalOffset(100);
            await LayoutAsync(host);
            Assert.True(text.HorizontalOffset > 0); // Long addresses remain reachable in rounded fields.
            text.Select(0, 3);
            text.SelectedText = "new";
            Assert.StartsWith("new", text.Text);
            foreach (bool? state in new bool?[] { false, true, null })
            {
                check.IsChecked = state;
                await LayoutAsync(host);
                var mark = (System.Windows.Shapes.Path)check.Template.FindName("CheckMark", check);
                Assert.Equal(state == false ? Visibility.Collapsed : Visibility.Visible, mark.Visibility);
            }
        }
        finally { host.Close(); }
    }

    private static void Wheel(UIElement source, int delta) => source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
        { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = source });

    private static async Task LayoutAsync(Window host)
    {
        await host.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();
    }
}
