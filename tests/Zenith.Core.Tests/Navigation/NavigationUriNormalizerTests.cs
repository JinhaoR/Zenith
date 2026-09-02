using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class NavigationUriNormalizerTests
{
    [Fact]
    public void TryNormalizeCanonicalizesSchemeHostPortAndPath()
    {
        var normalized = NavigationUriNormalizer.TryNormalize(
            "HTTPS://GitHub.COM.:443/a/../b?q=1#part",
            out var target);

        Assert.True(normalized);
        Assert.NotNull(target);
        Assert.Equal("github.com", target.Site.Host);
        Assert.Equal("https://github.com/b?q=1#part", target.Target.AbsoluteUri);
    }

    [Fact]
    public void TryNormalizePreservesNonDefaultPorts()
    {
        Assert.True(NavigationUriNormalizer.TryNormalize(
            "https://github.com:8443/path",
            out var target));

        Assert.NotNull(target);
        Assert.Equal(8443, target.Target.Port);
    }

    [Theory]
    [InlineData("ftp://github.com/")]
    [InlineData("https://user@github.com/")]
    [InlineData("github.com")]
    [InlineData("not a URI")]
    [InlineData("https://")]
    public void TryNormalizeRejectsUnsupportedOrAmbiguousTargets(string requestedTarget)
    {
        Assert.False(NavigationUriNormalizer.TryNormalize(requestedTarget, out var target));
        Assert.Null(target);
    }
}
