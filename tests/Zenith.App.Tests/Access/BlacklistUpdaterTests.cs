using System.Net;
using System.Net.Http;
using Zenith.App.Filtering;

namespace Zenith.App.Tests.Access;

public sealed class BlacklistUpdaterTests
{
    private static string ListText => "# Title: StevenBlack/hosts\n" + string.Join('\n', Enumerable.Range(0, 10000).Select(i => $"0.0.0.0 host{i}.example"));

    [Fact]
    public async Task UpdatesAreDailyAndFailedUpdatesRetainProtectedCache()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-BlacklistTests-");
        var handler = new Handler { Text = ListText };
        using var http = new HttpClient(handler);
        var clock = new Clock();
        try
        {
            using var updater = new BlacklistUpdater(folder.FullName, http, clock);
            Assert.Null(updater.Current);
            await updater.UpdateAsync();
            Assert.Equal(10000, updater.Current!.Count);
            await updater.UpdateAsync();
            Assert.Equal(1, handler.Requests);
            var previous = updater.Current;
            clock.Now = clock.Now.AddDays(1);
            handler.Text = "<html>upstream failure</html>";
            await updater.UpdateAsync();
            Assert.Same(previous, updater.Current);
            using var reopened = new BlacklistUpdater(folder.FullName, http, clock);
            Assert.Equal(10000, reopened.Current!.Count);
            Assert.DoesNotContain("host123.example", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder.FullName, "mandatory-hosts.bin"))));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task InvalidInitialDownloadNeverEnablesBrowsing()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-BlacklistTests-");
        try
        {
            using var http = new HttpClient(new Handler { Text = "0.0.0.0 one.example" });
            using var updater = new BlacklistUpdater(folder.FullName, http);
            await updater.UpdateAsync();
            Assert.Null(updater.Current);
            Assert.False(File.Exists(Path.Combine(folder.FullName, "mandatory-hosts.bin")));
        }
        finally { folder.Delete(true); }
    }

    [LiveBlacklistFact]
    [Trait("Category", "LiveBlacklist")]
    public async Task SelectedUpstreamVariantPassesValidation()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-BlacklistLiveTests-");
        try
        {
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
            using var updater = new BlacklistUpdater(folder.FullName, http);
            await updater.UpdateAsync();
            Assert.True(updater.Current?.Count >= 10000, updater.Status);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task CorruptCacheFailsClosedAndWriteFailureDoesNotPublishNewRules()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-BlacklistTests-");
        var handler = new Handler { Text = ListText };
        using var http = new HttpClient(handler);
        var clock = new Clock();
        try
        {
            using var updater = new BlacklistUpdater(folder.FullName, http, clock);
            await updater.UpdateAsync();
            var previous = updater.Current;
            Directory.CreateDirectory(Path.Combine(folder.FullName, "mandatory-hosts.bin.previous"));
            handler.Text = ListText + "\n0.0.0.0 additional.example";
            clock.Now = clock.Now.AddDays(1);
            await updater.UpdateAsync();
            Assert.Same(previous, updater.Current);
            using var reopened = new BlacklistUpdater(folder.FullName, http, clock);
            Assert.Equal(10000, reopened.Current!.Count);
            File.WriteAllBytes(Path.Combine(folder.FullName, "mandatory-hosts.bin"), [1, 2, 3]);
            using var corrupted = new BlacklistUpdater(folder.FullName, http, clock);
            Assert.Null(corrupted.Current);
        }
        finally { folder.Delete(true); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal string Text { get; set; } = "";
        internal int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(BlacklistUpdater.SourceUrl, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(Text) });
        }
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class LiveBlacklistFactAttribute : FactAttribute
{
    public LiveBlacklistFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ZENITH_BLACKLIST_LIVE_TESTS") != "1") Skip = "Opt-in live list download with an isolated cache.";
    }
}
