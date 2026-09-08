using System.Diagnostics.CodeAnalysis;
using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class SitePolicyNavigationEvaluatorTests
{
    private readonly SitePolicyNavigationEvaluator _evaluator = new(
        new FixedSitePolicySource(
            new SitePolicySnapshot(
            [
                new SitePolicyEntry(
                    "allowed.example",
                    AccessClass.Whitelist,
                    includeSubdomains: true),
                new SitePolicyEntry(
                    "blocked.example",
                    AccessClass.Blacklist,
                    includeSubdomains: true)
            ])));

    [Fact]
    public void WhitelistedNavigationIsAllowedWithCanonicalTarget()
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            "HTTPS://Docs.Allowed.Example.:443/a/../guide",
            NavigationOrigin.AddressBar));

        var allowed = Assert.IsType<NavigationDecision.Allowed>(decision);
        Assert.Equal(AccessClass.Whitelist, allowed.AccessClass);
        Assert.Equal("https://docs.allowed.example/guide", allowed.Target.AbsoluteUri);
    }

    [Fact]
    public void BlacklistedNavigationIsDeniedWithSpecificReason()
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            "https://cdn.blocked.example/",
            NavigationOrigin.WebView));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.Blacklisted, denied.Reason);
    }

    [Fact]
    public void UnclassifiedNavigationIsGreylisted()
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            "https://unknown.example/",
            NavigationOrigin.NewWindow));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.Greylisted, denied.Reason);
    }

    [Theory]
    [InlineData("not a URI")]
    [InlineData("ftp://allowed.example/")]
    [InlineData("https://user@allowed.example/")]
    public void UnsupportedTargetsAreDenied(string target)
    {
        var decision = _evaluator.Evaluate(new NavigationRequest(
            target,
            NavigationOrigin.AddressBar));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.UnsupportedTarget, denied.Reason);
    }

    [Fact]
    public void ConstructorRejectsAMissingPolicySource()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SitePolicyNavigationEvaluator(null!));
    }

    [Fact]
    public void EvaluateRejectsAMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(null!));
    }

    [Fact]
    public void UnavailablePolicySourceFailsClosed()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new UnavailablePolicySource());

        var decision = evaluator.Evaluate(new NavigationRequest(
            "https://allowed.example/",
            NavigationOrigin.AddressBar));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.PolicyUnavailable, denied.Reason);
    }

    [Fact]
    public void PolicySourceFailureFailsClosed()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new ThrowingPolicySource());

        var decision = evaluator.Evaluate(new NavigationRequest(
            "https://allowed.example/",
            NavigationOrigin.AddressBar));

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.PolicyUnavailable, denied.Reason);
    }

    [Fact]
    public void EveryNavigationReadsTheCurrentPolicySnapshot()
    {
        var source = new MutablePolicySource(new SitePolicySnapshot(
        [
            new SitePolicyEntry("allowed.example", AccessClass.Whitelist)
        ]));
        var evaluator = new SitePolicyNavigationEvaluator(source);
        var request = new NavigationRequest(
            "https://allowed.example/",
            NavigationOrigin.AddressBar);

        Assert.IsType<NavigationDecision.Allowed>(evaluator.Evaluate(request));

        source.Policy = new SitePolicySnapshot([], revision: 1);

        var denied = Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(request));
        Assert.Equal(NavigationDenialReason.Greylisted, denied.Reason);
    }

    private sealed class UnavailablePolicySource : ISitePolicySource
    {
        public bool TryGetActivePolicy(
            [NotNullWhen(true)] out SitePolicySnapshot? policy)
        {
            policy = null;
            return false;
        }
    }

    private sealed class ThrowingPolicySource : ISitePolicySource
    {
        public bool TryGetActivePolicy(
            [NotNullWhen(true)] out SitePolicySnapshot? policy)
        {
            policy = null;
            throw new InvalidOperationException("Simulated policy load failure.");
        }
    }

    private sealed class MutablePolicySource(SitePolicySnapshot policy) : ISitePolicySource
    {
        public SitePolicySnapshot Policy { get; set; } = policy;

        public bool TryGetActivePolicy(
            [NotNullWhen(true)] out SitePolicySnapshot? policy)
        {
            policy = Policy;
            return true;
        }
    }
}
