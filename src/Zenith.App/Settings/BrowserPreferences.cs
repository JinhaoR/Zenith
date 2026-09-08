using System.Text.Json.Serialization;

namespace Zenith.App.Settings;

public sealed record BrowserPreferences(bool StartSidebarExpanded = true, int DefaultZoomPercent = 100)
{
    public static IReadOnlyList<int> ZoomLevels { get; } = Array.AsReadOnly([75, 90, 100, 110, 125, 150]);

    [JsonIgnore]
    public bool IsValid => ZoomLevels.Contains(DefaultZoomPercent);
}
