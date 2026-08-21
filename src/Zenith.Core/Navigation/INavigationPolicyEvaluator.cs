namespace Zenith.Core.Navigation;

public interface INavigationPolicyEvaluator
{
    NavigationDecision Evaluate(NavigationRequest request);
}
