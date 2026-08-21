using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class UnavailableNavigationPolicyEvaluatorTests
{
    private readonly UnavailableNavigationPolicyEvaluator _evaluator = new();

    [Theory]
    [InlineData(NavigationOrigin.AddressBar)]
    [InlineData(NavigationOrigin.WebView)]
    [InlineData(NavigationOrigin.NewWindow)]
    public void EvaluateDeniesEveryNavigationOriginWhenPolicyIsUnavailable(NavigationOrigin origin)
    {
        var request = new NavigationRequest("https://example.com/", origin);

        var decision = _evaluator.Evaluate(request);

        var denied = Assert.IsType<NavigationDecision.Denied>(decision);
        Assert.Equal(NavigationDenialReason.PolicyUnavailable, denied.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a URI")]
    [InlineData("https://example.com/")]
    public void EvaluateFailsClosedForEveryTargetShape(string target)
    {
        var request = new NavigationRequest(target, NavigationOrigin.AddressBar);

        var decision = _evaluator.Evaluate(request);

        Assert.IsType<NavigationDecision.Denied>(decision);
    }

    [Fact]
    public void EvaluateRejectsAMissingRequest()
    {
        Assert.Throws<ArgumentNullException>(() => _evaluator.Evaluate(null!));
    }
}
