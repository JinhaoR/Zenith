using Microsoft.Web.WebView2.Core;

namespace Zenith.App.Extensions;

internal sealed record BrowserExtensionInfo(string Id, string Name, bool IsEnabled);

internal enum ContentProtectionStatus { Enabled, Disabled, NotInstalled, Unavailable }

internal static class ContentProtectionReader
{
    internal static async Task<ContentProtectionStatus> ReadAsync(CoreWebView2Profile? profile) =>
        await ReadAsync(async () =>
        {
            if (profile is null) throw new InvalidOperationException("No active browser profile.");
            var extensions = await profile.GetBrowserExtensionsAsync();
            return extensions.Select(extension => new BrowserExtensionInfo(extension.Id, extension.Name, extension.IsEnabled)).ToArray();
        });

    internal static async Task<ContentProtectionStatus> ReadAsync(
        Func<Task<IReadOnlyList<BrowserExtensionInfo>>> read, TimeSpan? timeout = null)
    {
        try
        {
            var extensions = await read().WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
            // Display metadata only, never an identity used to authorize extension code.
            var matches = extensions.Where(extension => extension.Name == "uBlock Origin Lite").ToArray();
            return matches.Length switch
            {
                0 => ContentProtectionStatus.NotInstalled,
                1 when !string.IsNullOrWhiteSpace(matches[0].Id) => matches[0].IsEnabled
                    ? ContentProtectionStatus.Enabled : ContentProtectionStatus.Disabled,
                _ => ContentProtectionStatus.Unavailable
            };
        }
        catch (Exception) { return ContentProtectionStatus.Unavailable; }
    }
}
