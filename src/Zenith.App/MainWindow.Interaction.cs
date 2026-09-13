using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zenith.App.Bookmarks;
using Zenith.Core.Navigation;

namespace Zenith.App;

public partial class MainWindow
{
    private void TabRow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || sender is not FrameworkElement { Tag: TabState tab }) return;
        e.Handled = true;
        CloseTab(tab);
    }

    private void Shortcut_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var middle = e.ChangedButton == MouseButton.Middle;
        var controlClick = e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!middle && !controlClick) return;
        var target = (sender as FrameworkElement)?.Tag switch
        {
            Bookmark bookmark => bookmark.Target,
            SphereResult result => result.Target,
            _ => null
        };
        if (target is null) return;
        e.Handled = true;
        _ = OpenShortcutTabAsync(target, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private async Task OpenShortcutTabAsync(Uri target, bool activate)
    {
        // Recheck before creation and again after async controller initialization.
        if (_navigationCoordinator.EvaluateNewWindowRequest(target.AbsoluteUri) is not NavigationDecision.Allowed)
        {
            ShowNotice("This destination is no longer available. Open it normally to see its current access options.");
            return;
        }
        await CreateTabCoreAsync(target, activate);
    }

    private ContextMenu CreateTabContextMenu(TabState tab)
    {
        var menu = new ContextMenu();
        var close = new MenuItem { Header = "Close tab", InputGestureText = "Middle-click" };
        close.Click += (_, _) => CloseTab(tab);
        var newTab = new MenuItem { Header = "New tab", InputGestureText = "Ctrl+T" };
        newTab.Click += (_, _) => _ = CreateTabAsync();
        menu.Items.Add(newTab);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        return menu;
    }
}
