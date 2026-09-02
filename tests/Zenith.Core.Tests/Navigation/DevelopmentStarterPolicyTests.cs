using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class DevelopmentStarterPolicyTests
{
    private readonly SitePolicyNavigationEvaluator _evaluator = new(
        new FixedSitePolicySource(DevelopmentStarterPolicy.Snapshot));

    [Theory]
    [InlineData("https://github.com/openai/")]
    [InlineData("https://www.github.com/openai/")]
    [InlineData("https://chatgpt.com/")]
    [InlineData("https://learn.microsoft.com/en-us/")]
    [InlineData("https://stackoverflow.com/questions/")]
    [InlineData("https://developer.mozilla.org/en-US/")]
    [InlineData("https://web.archive.org/")]
    public void StarterSitesAndTheirSubdomainsAreWhitelisted(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            target,
            NavigationOrigin.AddressBar));

        var allowed = Assert.IsType<NavigationDecision.Allowed>(decision);
        Assert.Equal(target, allowed.Target.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://notgithub.com/")]
    [InlineData("https://github.com.evil.example/")]
    public void OtherValidSitesAreGreylisted(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            target,
            NavigationOrigin.WebView));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.Greylisted, denied.Reason);
    }

    [Theory]
    [InlineData("ftp://github.com/")]
    [InlineData("not a URI")]
    [InlineData("https://github.com@evil.example/")]
    public void UnsupportedTargetsFailClosed(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            target,
            NavigationOrigin.WebView));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.UnsupportedTarget, denied.Reason);
    }

    [Fact]
    public void StarterPolicyReturnsTheCanonicalTarget()
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            "HTTPS://GitHub.COM.:443/openai/../dotnet",
            NavigationOrigin.AddressBar));

        var allowed = Assert.IsType<NavigationDecision.Allowed>(decision);
        Assert.Equal("https://github.com/dotnet", allowed.Target.AbsoluteUri);
    }

    [Fact]
    public void CatalogAndPolicyContainTheSameWhitelistEntries()
    {
        Assert.Equal(
            DevelopmentStarterPolicy.Sites.Count,
            DevelopmentStarterPolicy.Snapshot.Entries.Count);
        Assert.All(
            DevelopmentStarterPolicy.Snapshot.Entries,
            entry => Assert.Equal(AccessClass.Whitelist, entry.AccessClass));
    }
}
