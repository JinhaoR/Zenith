using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Zenith.App.Navigation;
using Zenith.Core.Navigation;
using Zenith.Core.Access;
using Zenith.App.Access;
using Zenith.Core.Vault;

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
    private ProtectedAccessStore? _accessStore;
    private Zenith.App.Filtering.BlacklistUpdater? _blacklist;
    private Zenith.App.Filtering.AdblockService? _adblock;
    private System.Net.Http.HttpClient? _listHttp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CaptureMidnightTheme();
        SystemParameters.StaticPropertyChanged += SystemParameters_OnStaticPropertyChanged;
        ApplyAccessibilityPalette();

        _accessStore = new ProtectedAccessStore();
        _listHttp = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(45) };
        _blacklist = new Zenith.App.Filtering.BlacklistUpdater(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "Filtering"), _listHttp);
        _adblock = new Zenith.App.Filtering.AdblockService(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "Filtering"), _listHttp);
        var policySource = new VaultService(_accessStore, blacklist: _blacklist);
        var accessService = new GreylistAccessService(policySource, _accessStore, _accessStore);
        var policyEvaluator = new SitePolicyNavigationEvaluator(policySource, accessService);
        var navigationCoordinator = new NavigationCoordinator(policyEvaluator);
        var mainWindow = new MainWindow(navigationCoordinator, accessService, policySource, _blacklist, _adblock);

        MainWindow = mainWindow;
        mainWindow.Show();
        _blacklist.Changed += () => Dispatcher.BeginInvoke(() => mainWindow.ApplyBlacklistUpdate());
        _ = Task.Run(_blacklist.RunAsync);
        _ = Task.Run(_adblock.RunAsync);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParameters_OnStaticPropertyChanged;
        _accessStore?.Dispose();
        _blacklist?.Dispose();
        _adblock?.Dispose();
        _listHttp?.Dispose();
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
        }
        foreach (Window window in Windows)
        {
            WindowFrameAppearance.Apply(window);
        }
    }
}
