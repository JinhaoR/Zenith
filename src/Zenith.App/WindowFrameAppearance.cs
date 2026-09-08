using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Zenith.App;

internal static class WindowFrameAppearance
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            window.TryFindResource("ZenithChromeBrush") is not SolidColorBrush chromeBrush ||
            window.TryFindResource("ZenithTextBrush") is not SolidColorBrush textBrush)
        {
            return;
        }

        try
        {
            var darkMode = 1;
            var captionColor = ToColorRef(chromeBrush.Color);
            var textColor = ToColorRef(textBrush.Color);
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
