using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App;

public partial class App : Application
{
    private static readonly string[] ThemeResourceKeys =
    [
        "ZenithWindowBrush",
        "ZenithChromeBrush",
        "ZenithAmbientBrush",
        "ZenithSurfaceBrush",
        "ZenithRaisedSurfaceBrush",
        "ZenithHoverBrush",
        "ZenithBorderBrush",
        "ZenithInteractiveBorderBrush",
        "ZenithTextBrush",
        "ZenithMutedTextBrush",
        "ZenithSubtleTextBrush",
        "ZenithAccentBrush",
        "ZenithAccentHoverBrush",
        "ZenithAccentPressedBrush",
        "ZenithAccentForegroundBrush",
        "ZenithHighlightForegroundBrush",
        "ZenithFocusBrush",
        "ZenithFocusOnAccentBrush"
    ];

    private readonly Dictionary<string, object> _midnightThemeResources = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CaptureMidnightTheme();
        SystemParameters.StaticPropertyChanged += SystemParameters_OnStaticPropertyChanged;
        ApplyAccessibilityPalette();

        var policyEvaluator = new StarterWhitelistNavigationPolicyEvaluator();
        var navigationCoordinator = new NavigationCoordinator(policyEvaluator);
        var mainWindow = new MainWindow(navigationCoordinator);

        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParameters_OnStaticPropertyChanged;
        base.OnExit(e);
    }

    private void SystemParameters_OnStaticPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            ApplyAccessibilityPalette();
        }
    }

    private void CaptureMidnightTheme()
    {
        foreach (var key in ThemeResourceKeys)
        {
            _midnightThemeResources[key] = Resources[key];
        }
    }

    private void ApplyAccessibilityPalette()
    {
        if (!SystemParameters.HighContrast)
        {
            foreach (var (key, value) in _midnightThemeResources)
            {
                Resources[key] = value;
            }

            UpdateMainWindowHostBackground();

            return;
        }

        SetHighContrastBrush("ZenithWindowBrush", SystemColors.WindowBrush);
        SetHighContrastBrush("ZenithChromeBrush", SystemColors.WindowBrush);
        SetHighContrastBrush("ZenithAmbientBrush", SystemColors.WindowBrush);
        SetHighContrastBrush("ZenithSurfaceBrush", SystemColors.ControlBrush);
        SetHighContrastBrush("ZenithRaisedSurfaceBrush", SystemColors.ControlBrush);
        SetHighContrastBrush("ZenithHoverBrush", SystemColors.HighlightBrush);
        SetHighContrastBrush("ZenithBorderBrush", SystemColors.ActiveBorderBrush);
        SetHighContrastBrush("ZenithInteractiveBorderBrush", SystemColors.WindowTextBrush);
        SetHighContrastBrush("ZenithTextBrush", SystemColors.WindowTextBrush);
        SetHighContrastBrush("ZenithMutedTextBrush", SystemColors.WindowTextBrush);
        SetHighContrastBrush("ZenithSubtleTextBrush", SystemColors.WindowTextBrush);
        SetHighContrastBrush("ZenithAccentBrush", SystemColors.HighlightBrush);
        SetHighContrastBrush("ZenithAccentHoverBrush", SystemColors.HighlightBrush);
        SetHighContrastBrush("ZenithAccentPressedBrush", SystemColors.HighlightBrush);
        SetHighContrastBrush("ZenithAccentForegroundBrush", SystemColors.HighlightTextBrush);
        SetHighContrastBrush("ZenithHighlightForegroundBrush", SystemColors.HighlightTextBrush);
        SetHighContrastBrush("ZenithFocusBrush", SystemColors.HighlightBrush);
        SetHighContrastBrush("ZenithFocusOnAccentBrush", SystemColors.HighlightTextBrush);

        UpdateMainWindowHostBackground();
    }

    private void SetHighContrastBrush(string key, Brush brush) => Resources[key] = brush;

    private void UpdateMainWindowHostBackground()
    {
        if (MainWindow is MainWindow mainWindow)
        {
            mainWindow.UpdateBrowserHostBackground();
            mainWindow.UpdateWindowFrameAppearance();
        }
    }
}
