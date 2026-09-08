using Zenith.Core.Filtering;
using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class HostsBlacklistTests
{
    [Fact]
    public void CanonicalExactHostsOverrideWhitelistWithoutBlockingLookalikes()
    {
        var list = HostsBlacklist.Parse("# comment\n127.0.0.1 localhost\n0.0.0.0 BAD.EXAMPLE. bad.example # duplicate\n");
        Assert.Equal(1, list.Count);
        var policy = new SitePolicySnapshot([new("bad.example", AccessClass.Whitelist, true)], mandatoryBlacklist: list);
        Assert.True(SiteIdentity.TryCreate("bad.example", out var site));
        Assert.Equal(AccessClass.Blacklist, policy.Classify(site));
        Assert.True(BlacklistRequestPolicy.IsBlocked(list, "https://BAD.EXAMPLE./ad.js"));
        Assert.True(BlacklistRequestPolicy.IsBlocked(list, "wss://bad.example/socket"));
        Assert.False(BlacklistRequestPolicy.IsBlocked(list, "https://sub.bad.example/"));
        Assert.False(BlacklistRequestPolicy.IsBlocked(list, "https://notbad.example/"));
        Assert.True(BlacklistRequestPolicy.IsBlocked(null, "https://github.com/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>server error</html>")]
    [InlineData("0.0.0.0 https://bad.example/path")]
    [InlineData("1.2.3.4 bad.example")]
    [InlineData("0.0.0.0 *.example")]
    public void InvalidListsCannotBecomeEmptyOrPartialProtection(string text) =>
        Assert.Throws<ArgumentException>(() => HostsBlacklist.Parse(text));
}
