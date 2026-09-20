using Zenith.App.Extensions;

namespace Zenith.App.Tests.Extensions;

public sealed class ContentProtectionReaderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InstalledExtensionReportsNativeEnabledState(bool enabled)
    {
        Assert.Equal(enabled ? ContentProtectionStatus.Enabled : ContentProtectionStatus.Disabled, await ContentProtectionReader.ReadAsync(() => Task.FromResult<IReadOnlyList<BrowserExtensionInfo>>(
            [new("fixture-id", "uBlock Origin Lite", enabled)])));
    }

    [Fact]
    public async Task UnrelatedExtensionsDoNotIndicateProtection()
    {
        Assert.Equal(ContentProtectionStatus.NotInstalled, await Read([]));
        Assert.Equal(ContentProtectionStatus.NotInstalled, await Read([new("pdf", "PDF Viewer", true)]));
    }

    [Fact]
    public async Task AmbiguousOrInvalidMetadataIsUnavailable()
    {
        Assert.Equal(ContentProtectionStatus.Unavailable, await Read([
            new("one", "uBlock Origin Lite", true), new("two", "uBlock Origin Lite", false)]));
        Assert.Equal(ContentProtectionStatus.Unavailable, await Read([new("", "uBlock Origin Lite", true)]));
    }

    [Fact]
    public async Task FailureOrHungEnumerationDoesNotEscapeIntoSettings()
    {
        Assert.Equal(ContentProtectionStatus.Unavailable, await ContentProtectionReader.ReadAsync(() => throw new InvalidOperationException()));
        var pending = new TaskCompletionSource<IReadOnlyList<BrowserExtensionInfo>>();
        Assert.Equal(ContentProtectionStatus.Unavailable, await ContentProtectionReader.ReadAsync(() => pending.Task, TimeSpan.Zero));
        pending.SetResult([]);
        Assert.Equal(ContentProtectionStatus.NotInstalled, await Read([]));
    }

    private static Task<ContentProtectionStatus> Read(IReadOnlyList<BrowserExtensionInfo> values) =>
        ContentProtectionReader.ReadAsync(() => Task.FromResult(values));
}
