using Microsoft.Web.WebView2.Core;

namespace Zenith.App.BrowserData;

internal enum BrowserDataKind { All, Cookies, Cache, History, SiteData }

internal interface IBrowserDataService
{
    Task ClearAsync(BrowserDataKind kind);
}

internal sealed class BrowserDataService : IBrowserDataService
{
    private readonly Func<CoreWebView2BrowsingDataKinds, Task> _clear;

    internal BrowserDataService(CoreWebView2Profile profile) : this(kinds => profile.ClearBrowsingDataAsync(kinds)) { }

    internal BrowserDataService(Func<CoreWebView2BrowsingDataKinds, Task> clear) => _clear = clear;

    public Task ClearAsync(BrowserDataKind kind) =>
        _clear(DataKinds(kind)).WaitAsync(TimeSpan.FromSeconds(60));

    internal static CoreWebView2BrowsingDataKinds DataKinds(BrowserDataKind kind) => kind switch
    {
        BrowserDataKind.All => CoreWebView2BrowsingDataKinds.AllProfile,
        BrowserDataKind.Cookies => CoreWebView2BrowsingDataKinds.Cookies,
        BrowserDataKind.Cache => CoreWebView2BrowsingDataKinds.DiskCache,
        BrowserDataKind.History => CoreWebView2BrowsingDataKinds.BrowsingHistory,
        BrowserDataKind.SiteData => CoreWebView2BrowsingDataKinds.AllSite,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    // Startup security cleanup is deliberately separate from user-selected clearing.
    internal Task ClearServiceWorkersAsync() =>
        _clear(CoreWebView2BrowsingDataKinds.ServiceWorkers).WaitAsync(TimeSpan.FromSeconds(30));

    internal static string Description(BrowserDataKind kind) => kind switch
    {
        BrowserDataKind.All => "Remove all browsing data supported by WebView2 for this profile, including cookies, site storage, cache, history, saved autofill/password data and browser permission settings.",
        BrowserDataKind.Cookies => "Remove cookies for all websites in this profile. This can sign you out. Other site storage is kept, so this is not a complete session reset.",
        BrowserDataKind.Cache => "Remove the browser's disk cache. Cookies and site storage, including websites' Cache Storage, are kept.",
        BrowserDataKind.History => "Remove WebView2 browsing history for this profile. Cookies, site storage and downloaded files are kept.",
        BrowserDataKind.SiteData => "Remove cookies and all site storage supported by WebView2, including local storage, IndexedDB, Cache Storage and service workers. This can sign you out and delete offline website work. History and the browser's disk cache are kept.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
