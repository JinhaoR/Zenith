using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Zenith.App.Settings;

/// <summary>Chain nested settings scrolling without changing closed dropdown values.</summary>
internal static class SettingsScrollBehavior
{
    internal static void HandleWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin) return;
        var combo = Ancestor<ComboBox>(origin);
        if (combo?.IsDropDownOpen == true) return;
        var viewer = Ancestor<ScrollViewer>(origin);
        while (viewer is not null)
        {
            if (TryScroll(viewer, e.Delta)) { e.Handled = true; return; }
            viewer = Ancestor<ScrollViewer>(Parent(viewer));
        }
        // At the page edge, a closed ComboBox must not consume the wheel as selection.
        if (combo is not null) e.Handled = true;
    }

    internal static bool TryScroll(ScrollViewer viewer, int delta)
    {
        if (delta == 0 || viewer.ScrollableHeight <= 0 ||
            (delta > 0 ? viewer.VerticalOffset <= 0 : viewer.VerticalOffset >= viewer.ScrollableHeight)) return false;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return true;
        if (lines < 0)
        {
            if (delta > 0) viewer.PageUp(); else viewer.PageDown();
        }
        else if (viewer.CanContentScroll)
        {
            for (var i = 0; i < Math.Max(1, Math.Abs(delta) * lines / 120); i++)
                if (delta > 0) viewer.LineUp(); else viewer.LineDown();
        }
        else viewer.ScrollToVerticalOffset(viewer.VerticalOffset - delta / 120d * lines * 16);
        return true;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        for (; node is not null; node = Parent(node))
            if (node is T match) return match;
        return null;
    }

    private static DependencyObject? Parent(DependencyObject node) => node is Visual
        ? VisualTreeHelper.GetParent(node)
        : node is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(node);
}
