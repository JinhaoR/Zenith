using Microsoft.Web.WebView2.Core;
using Zenith.App.BrowserData;

namespace Zenith.App.Tests.BrowserData;

public sealed class BrowserDataServiceTests
{
    [Fact]
    public async Task EachActionUsesOnlyItsDocumentedNativeDataKind()
    {
        CoreWebView2BrowsingDataKinds? observed = null;
        IBrowserDataService service = new BrowserDataService(value => { observed = value; return Task.CompletedTask; });
        var expected = new Dictionary<BrowserDataKind, CoreWebView2BrowsingDataKinds>
        {
            [BrowserDataKind.All] = CoreWebView2BrowsingDataKinds.AllProfile,
            [BrowserDataKind.Cookies] = CoreWebView2BrowsingDataKinds.Cookies,
            [BrowserDataKind.Cache] = CoreWebView2BrowsingDataKinds.DiskCache,
            [BrowserDataKind.History] = CoreWebView2BrowsingDataKinds.BrowsingHistory,
            [BrowserDataKind.SiteData] = CoreWebView2BrowsingDataKinds.AllSite
        };
        foreach (var (kind, native) in expected)
        {
            await service.ClearAsync(kind);
            Assert.Equal(native, observed);
            Assert.NotEmpty(BrowserDataService.Description(kind));
        }
    }

    [Fact]
    public async Task InvalidRequestsAndNativeFailuresDoNotReportSuccessfulClearing()
    {
        var calls = 0;
        var service = new BrowserDataService(_ => { calls++; return Task.FromException(new InvalidOperationException("Fixture failure")); });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ClearAsync((BrowserDataKind)999));
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAsync(BrowserDataKind.All));
        Assert.Equal(1, calls);
    }
}
