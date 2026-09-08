namespace Zenith.App.Settings;

internal static class DurationText
{
    public static string Format(int seconds) => seconds switch
    {
        >= 86400 when seconds % 86400 == 0 => $"{seconds / 86400} day{(seconds == 86400 ? "" : "s")}",
        >= 3600 when seconds % 3600 == 0 => $"{seconds / 3600} hour{(seconds == 3600 ? "" : "s")}",
        >= 60 when seconds % 60 == 0 => $"{seconds / 60} minute{(seconds == 60 ? "" : "s")}",
        _ => $"{seconds} seconds"
    };
}
