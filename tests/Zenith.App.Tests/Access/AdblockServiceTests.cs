using System.Net;
using System.Net.Http;
using Zenith.App.Filtering;
using Zenith.Core.Filtering;

namespace Zenith.App.Tests.Access;

public sealed class AdblockServiceTests
{
    private static readonly ResourceRequest TestRequest = new("https://zenith-filter-test.example/banner.js", "https://example.org/", ResourceKind.Script);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 9, 7, 22, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;
        public bool InvalidPrivacy;
        public bool Throw;
        public bool Oversized;
        public bool Redirected;
        public string Suffix = "\n||zenith-filter-test.example^";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw) throw new HttpRequestException();
            var privacy = request.RequestUri!.AbsoluteUri == AdblockService.EasyPrivacyUrl;
            var body = privacy && InvalidPrivacy ? "<html>Upstream error</html>" : AdblockEngine.ReadAsset(privacy ? "easyprivacy.txt" : "easylist.txt") + Suffix;
            var content = new StringContent(body);
            if (Oversized) content.Headers.ContentLength = 17 * 1024 * 1024;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Redirected ? new HttpRequestMessage(HttpMethod.Get, "https://untrusted.example/list") : request,
                Content = content
            });
        }
    }

    [Fact]
    public async Task UpdateIsAtomicDailyAndSurvivesOfflineRestart()
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-AdblockTests-");
        var clock = new Clock();
        var handler = new Handler();
        using var http = new HttpClient(handler);
        try
        {
            using (var service = new AdblockService(directory.FullName, http, clock))
            {
                Assert.Contains("Bundled snapshots", service.Status);
                Assert.False(service.Blocks(TestRequest));
                await service.UpdateAsync();
                Assert.True(service.Blocks(TestRequest));
                Assert.Equal(2, handler.Calls);
                await service.UpdateAsync();
                Assert.Equal(2, handler.Calls);
                clock.Now += TimeSpan.FromDays(1);
                handler.InvalidPrivacy = true;
                handler.Suffix = "\n||different-filter-test.example^";
                await service.UpdateAsync();
                Assert.True(service.Blocks(TestRequest));
                Assert.False(service.Blocks(TestRequest with { Url = "https://different-filter-test.example/banner.js" }));
                Assert.Contains("update failed", service.Status);
            }
            handler.Throw = true;
            using var restarted = new AdblockService(directory.FullName, http, clock);
            Assert.True(restarted.Blocks(TestRequest));
            await restarted.UpdateAsync();
            Assert.True(restarted.Blocks(TestRequest));
            var bytes = File.ReadAllBytes(Path.Combine(directory.FullName, "resource-filters.bin"));
            Assert.DoesNotContain("zenith-filter-test", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task CorruptCacheAndWriteFailureRetainBundledProtection()
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-AdblockTests-");
        using var http = new HttpClient(new Handler());
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "resource-filters.bin"), "invalid protected cache");
            Directory.CreateDirectory(Path.Combine(directory.FullName, "resource-filters.bin.previous"));
            using var service = new AdblockService(directory.FullName, http, new Clock());
            Assert.Contains("Bundled snapshots", service.Status);
            await service.UpdateAsync();
            Assert.False(service.Blocks(TestRequest));
            Assert.True(service.Blocks(TestRequest with { Url = "https://ad.doubleclick.net/banner.js" }));
            Assert.Contains("update failed", service.Status);
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedOrUnexpectedSourceCannotReplaceFilters(bool oversized)
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-AdblockTests-");
        using var http = new HttpClient(new Handler { Oversized = oversized, Redirected = !oversized });
        try
        {
            using var service = new AdblockService(directory.FullName, http, new Clock());
            await service.UpdateAsync();
            Assert.Contains("update failed", service.Status);
            Assert.False(service.Blocks(TestRequest));
            Assert.False(File.Exists(Path.Combine(directory.FullName, "resource-filters.bin")));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task CancelledUpdateDoesNotPublishOrDownload()
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-AdblockTests-");
        var handler = new Handler();
        using var http = new HttpClient(handler);
        try
        {
            using var service = new AdblockService(directory.FullName, http, new Clock());
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync(stop.Token));
            Assert.Equal(0, handler.Calls);
            Assert.False(service.Blocks(TestRequest));
        }
        finally { directory.Delete(true); }
    }

    [LiveAdblockFact]
    [Trait("Category", "LiveAdblock")]
    public async Task OfficialSubscriptionsCanBeDownloadedValidatedAndReopened()
    {
        var directory = Directory.CreateTempSubdirectory("Zenith-AdblockLive-");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
        try
        {
            using (var service = new AdblockService(directory.FullName, http))
            {
                await service.UpdateAsync();
                Assert.DoesNotContain("failed", service.Status);
                Assert.DoesNotContain("Bundled snapshots", service.Status);
                Assert.True(File.Exists(Path.Combine(directory.FullName, "resource-filters.bin")));
            }
            using var reopened = new AdblockService(directory.FullName, http);
            Assert.DoesNotContain("Bundled snapshots", reopened.Status);
            Assert.True(reopened.Blocks(TestRequest with { Url = "https://ad.doubleclick.net/banner.js" }));
        }
        finally { directory.Delete(true); }
    }
}

public sealed class LiveAdblockFactAttribute : FactAttribute
{
    public LiveAdblockFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_ADBLOCK_LIVE_TESTS") != "1") Skip = "Opt-in official subscription downloads with an isolated cache.";
    }
}
