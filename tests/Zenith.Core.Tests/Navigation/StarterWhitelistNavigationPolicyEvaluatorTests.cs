using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class StarterWhitelistNavigationPolicyEvaluatorTests
{
    private readonly StarterWhitelistNavigationPolicyEvaluator _evaluator = new();

    [Theory]
    [InlineData("https://github.com/openai/" )]
    [InlineData("https://www.github.com/openai/" )]
    [InlineData("https://chatgpt.com/" )]
    [InlineData("https://learn.microsoft.com/en-us/" )]
    [InlineData("https://stackoverflow.com/questions/" )]
    [InlineData("https://developer.mozilla.org/en-US/" )]
    [InlineData("https://web.archive.org/" )]
    public void EvaluateAllowsStarterSitesAndTheirSubdomains(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(target, NavigationOrigin.AddressBar));

        var allowed = Assert.IsType<NavigationDecision.Allowed>(decision);
        Assert.Equal(target, allowed.Target.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://notgithub.com/")]
    [InlineData("https://github.com.evil.example/")]
    [InlineData("https://github.com@evil.example/")]
    [InlineData("ftp://github.com/")]
    [InlineData("not a URI")]
    public void EvaluateDeniesTargetsOutsideTheStarterWhitelist(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(target, NavigationOrigin.WebView));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.NotWhitelisted, denied.Reason);
    }

    [Fact]
    public void EvaluateReturnsTheCanonicalTarget()
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            "HTTPS://GitHub.COM.:443/openai/../dotnet",
            NavigationOrigin.AddressBar));

        var allowed = Assert.IsType<NavigationDecision.Allowed>(decision);
        Assert.Equal("https://github.com/dotnet", allowed.Target.AbsoluteUri);
    }

    [Fact]
    public void EvaluateHonorsAnExactOnlyStarterSite()
    {
        var site = new StarterWhitelistSite(
            "Example",
            "example.com",
            "https://example.com/",
            includeSubdomains: false);
        var evaluator = new StarterWhitelistNavigationPolicyEvaluator([site]);

        var exact = evaluator.Evaluate(new NavigationRequest(
            "https://example.com/",
            NavigationOrigin.AddressBar));
        var subdomain = evaluator.Evaluate(new NavigationRequest(
            "https://www.example.com/",
            NavigationOrigin.AddressBar));

        Assert.IsType<NavigationDecision.Allowed>(exact);
        Assert.IsType<NavigationDecision.Denied>(subdomain);
    }
}
