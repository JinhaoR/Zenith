using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class StarterWhitelistSiteTests
{
    [Theory]
    [InlineData("https://example.com/", false)]
    [InlineData("https://www.example.com/", true)]
    [InlineData("https://nested.www.example.com/article", true)]
    public void LaunchTargetCanUseTheConfiguredHostScope(string target, bool includeSubdomains)
    {
        var site = new StarterWhitelistSite("Example", "example.com", target, includeSubdomains);
        Assert.Equal("example.com", site.Host);
        Assert.Equal(target, site.Target.AbsoluteUri);
        Assert.Equal(includeSubdomains, site.IncludeSubdomains);
    }

    [Theory]
    [InlineData("https://www.example.com/", false)]
    [InlineData("https://notexample.com/", true)]
    [InlineData("https://example.com.evil.test/", true)]
    [InlineData("https://user@example.com/", true)]
    [InlineData("ftp://example.com/", true)]
    [InlineData("example.com", true)]
    public void InvalidOrOutOfScopeLaunchTargetsAreRejected(string target, bool includeSubdomains)
    {
        Assert.Throws<ArgumentException>(() => new StarterWhitelistSite("Example", "example.com", target, includeSubdomains));
    }

    [Fact]
    public void EntireStarterCatalogInitializesAndEveryLaunchTargetIsWhitelisted()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(DevelopmentStarterPolicy.Snapshot));
        Assert.NotEmpty(DevelopmentStarterPolicy.Sites);
        foreach (var site in DevelopmentStarterPolicy.Sites)
        {
            var allowed = Assert.IsType<NavigationDecision.Allowed>(
                evaluator.Evaluate(new(site.Target.AbsoluteUri, NavigationOrigin.AddressBar)));
            Assert.Equal(site.Target, allowed.Target);
        }
    }
}
