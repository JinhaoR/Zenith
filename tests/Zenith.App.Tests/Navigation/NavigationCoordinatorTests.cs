using Zenith.App.Navigation;
using Zenith.Core.Navigation;

namespace Zenith.App.Tests.Navigation;

public sealed class NavigationCoordinatorTests
{
    [Theory]
    [InlineData(NavigationOrigin.AddressBar)]
    [InlineData(NavigationOrigin.WebView)]
    [InlineData(NavigationOrigin.NewWindow)]
    public void EveryEntryPointUsesTheSamePolicyEvaluator(NavigationOrigin origin)
    {
        var expectedDecision = new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
        var evaluator = new RecordingPolicyEvaluator(expectedDecision);
        var coordinator = new NavigationCoordinator(evaluator);

        var actualDecision = origin switch
        {
            NavigationOrigin.AddressBar => coordinator.EvaluateAddressBarRequest("https://example.com/"),
            NavigationOrigin.WebView => coordinator.EvaluateWebViewRequest("https://example.com/"),
            NavigationOrigin.NewWindow => coordinator.EvaluateNewWindowRequest("https://example.com/"),
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        Assert.Same(expectedDecision, actualDecision);
        var request = Assert.Single(evaluator.Requests);
        Assert.Equal("https://example.com/", request.Target);
        Assert.Equal(origin, request.Origin);
    }

    [Fact]
    public void ConstructorRejectsAMissingPolicyEvaluator()
    {
        Assert.Throws<ArgumentNullException>(() => new NavigationCoordinator(null!));
    }

    private sealed class RecordingPolicyEvaluator(NavigationDecision decision) : INavigationPolicyEvaluator
    {
        public List<NavigationRequest> Requests { get; } = [];

        public NavigationDecision Evaluate(NavigationRequest request)
        {
            Requests.Add(request);
            return decision;
        }
    }
}
