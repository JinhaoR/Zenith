namespace Zenith.Core.Navigation;

public sealed class UnavailableNavigationPolicyEvaluator : INavigationPolicyEvaluator
{
    public NavigationDecision Evaluate(NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
    }
}
