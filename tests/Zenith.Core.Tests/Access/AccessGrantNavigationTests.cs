using Zenith.Core.Access;
using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Access;

public sealed class AccessGrantNavigationTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    public void GrantIsValidOnlyWithinItsExplicitLifetime(int elapsedMinutes, bool allowed)
    {
        var evaluator = CreateEvaluator(new FixedGrantSource(Grant()), Start.AddMinutes(elapsedMinutes));
        var decision = evaluator.Evaluate(new NavigationRequest("https://example.com/", NavigationOrigin.WebView));
        Assert.Equal(allowed, decision is NavigationDecision.Allowed);
    }

    [Theory]
    [InlineData("https://sub.example.com/")]
    [InlineData("https://notexample.com/")]
    [InlineData("https://example.com.evil.test/")]
    [InlineData("https://other.test/")]
    public void GrantCannotAuthorizeAnotherHostnameEvenIfSourceReturnsIt(string target)
    {
        var evaluator = CreateEvaluator(new FixedGrantSource(Grant()), Start);
        var denial = Assert.IsType<NavigationDecision.Denied>(
            evaluator.Evaluate(new NavigationRequest(target, NavigationOrigin.WebView)));
        Assert.Equal(NavigationDenialReason.Greylisted, denial.Reason);
    }

    [Fact]
    public void GrantDoesNotChangeClassificationOrOverrideBlacklist()
    {
        var source = new FixedGrantSource(Grant());
        var policy = new SitePolicySnapshot([]);
        var evaluator = CreateEvaluator(source, Start, policy);
        var allowed = Assert.IsType<NavigationDecision.Allowed>(evaluator.Evaluate(
            new NavigationRequest("https://example.com/page", NavigationOrigin.AddressBar)));
        Assert.Equal(AccessClass.Greylist, allowed.AccessClass);
        Assert.Equal(AccessClass.Greylist, policy.Classify(Grant().Site));

        var blacklisted = CreateEvaluator(source, Start, new SitePolicySnapshot(
            [new SitePolicyEntry("example.com", AccessClass.Blacklist)]));
        var denial = Assert.IsType<NavigationDecision.Denied>(blacklisted.Evaluate(
            new NavigationRequest("https://example.com/", NavigationOrigin.NewWindow)));
        Assert.Equal(NavigationDenialReason.Blacklisted, denial.Reason);
    }

    [Fact]
    public void GrantSourceFailureDeniesNavigation()
    {
        var evaluator = CreateEvaluator(new FailingGrantSource(), Start);
        var denial = Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(
            new NavigationRequest("https://example.com/", NavigationOrigin.WebView)));
        Assert.Equal(NavigationDenialReason.PolicyUnavailable, denial.Reason);
    }

    [Fact]
    public void ExpiredGrantIsRecheckedOnTheNextNavigation()
    {
        var clock = new ManualClock(Start);
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot([])),
            new FixedGrantSource(Grant()), clock);
        var request = new NavigationRequest("https://example.com/", NavigationOrigin.WebView);
        Assert.IsType<NavigationDecision.Allowed>(evaluator.Evaluate(request));
        clock.Now = Start.AddMinutes(30);
        Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(request));
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("https://user@example.com/")]
    [InlineData("file:///example.com")]
    public void GrantCannotAuthorizeUnsupportedTargets(string target)
    {
        var evaluator = CreateEvaluator(new FixedGrantSource(Grant()), Start);
        var denial = Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(
            new NavigationRequest(target, NavigationOrigin.WebView)));
        Assert.Equal(NavigationDenialReason.UnsupportedTarget, denial.Reason);
    }

    [Fact]
    public void InvalidLifetimeCannotCreateAGrant()
    {
        var site = Grant().Site;
        Assert.Throws<ArgumentException>(() => new AccessGrant(site, Start, Start));
        Assert.Throws<ArgumentException>(() => new AccessGrant(site, Start, Start.AddSeconds(-1)));
    }

    private static AccessGrant Grant()
    {
        Assert.True(SiteIdentity.TryCreate("example.com", out var site));
        return new AccessGrant(site, Start, Start.AddMinutes(30));
    }

    private static SitePolicyNavigationEvaluator CreateEvaluator(IAccessGrantSource source,
        DateTimeOffset now, SitePolicySnapshot? policy = null) =>
        new(new FixedSitePolicySource(policy ?? new SitePolicySnapshot([])), source, new ManualClock(now));

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FixedGrantSource(AccessGrant value) : IAccessGrantSource
    {
        public bool TryGetGrant(SiteIdentity site, out AccessGrant? grant)
        {
            grant = value;
            return true;
        }
    }

    private sealed class FailingGrantSource : IAccessGrantSource
    {
        public bool TryGetGrant(SiteIdentity site, out AccessGrant? grant) =>
            throw new InvalidOperationException("Test storage failure");
    }
}
