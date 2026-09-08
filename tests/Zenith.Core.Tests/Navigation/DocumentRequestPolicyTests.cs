using Zenith.Core.Navigation;
using Zenith.Core.Access;

namespace Zenith.Core.Tests.Navigation;

public sealed class DocumentRequestPolicyTests
{
    private readonly DocumentRequestPolicy _policy = new(new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
        [new SitePolicyEntry("sphere.example", AccessClass.Whitelist), new SitePolicyEntry("blocked.example", AccessClass.Blacklist)]))));

    [Theory]
    [InlineData("https://unknown.example/", true, "https://sphere.example/", false)]
    [InlineData("https://unknown.example/", false, "https://sphere.example/", true)]
    [InlineData("https://blocked.example/", false, "https://sphere.example/", false)]
    [InlineData("https://sphere.example/", false, "https://unknown.example/", false)]
    [InlineData("https://unknown.example/", false, null, false)]
    [InlineData("file:///local", false, "https://sphere.example/", false)]
    [InlineData("https://sphere.example/", true, null, true)]
    public void DocumentsPreserveIndependentNavigationAndEmbeddedCompatibility(string target, bool main, string? parent, bool allowed) =>
        Assert.Equal(allowed, _policy.Evaluate(target, main, parent) is NavigationDecision.Allowed);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Grants(AccessGrant grant) : IAccessGrantSource
    {
        public bool TryGetGrant(SiteIdentity site, out AccessGrant? value) { value = grant; return true; }
    }
    [Fact]
    public void TemporaryPageDoesNotAuthorizeOtherHostsAndExpiryRevokesItsFrames()
    {
        var clock = new Clock();
        Assert.True(SiteIdentity.TryCreate("temporary.example", out var site));
        var grants = new Grants(new AccessGrant(site!, clock.Now, clock.Now.AddMinutes(1)));
        var policy = new DocumentRequestPolicy(new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("sphere.example", AccessClass.Whitelist)])), grants, clock));
        Assert.IsType<NavigationDecision.Allowed>(policy.Evaluate("https://temporary.example/frame", false, "https://temporary.example/"));
        Assert.IsType<NavigationDecision.Denied>(policy.Evaluate("https://other.example/", false, "https://temporary.example/"));
        clock.Now += TimeSpan.FromMinutes(1);
        Assert.IsType<NavigationDecision.Denied>(policy.Evaluate("https://sphere.example/", false, "https://temporary.example/"));
    }

    private sealed class FailingEvaluator : INavigationPolicyEvaluator
    {
        public NavigationDecision Evaluate(NavigationRequest request) => throw new IOException("Unavailable");
    }
    [Fact]
    public void EvaluationFailureNeverResumesADocument() =>
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable),
            new DocumentRequestPolicy(new FailingEvaluator()).Evaluate("https://sphere.example/", true, null));
}
