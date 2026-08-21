using Zenith.Core.Navigation;

namespace Zenith.App.Navigation;

public sealed class NavigationCoordinator
{
    private readonly INavigationPolicyEvaluator _policyEvaluator;

    public NavigationCoordinator(INavigationPolicyEvaluator policyEvaluator)
    {
        ArgumentNullException.ThrowIfNull(policyEvaluator);
        _policyEvaluator = policyEvaluator;
    }

    public NavigationDecision EvaluateAddressBarRequest(string target) =>
        Evaluate(target, NavigationOrigin.AddressBar);

    public NavigationDecision EvaluateWebViewRequest(string target) =>
        Evaluate(target, NavigationOrigin.WebView);

    public NavigationDecision EvaluateNewWindowRequest(string target) =>
        Evaluate(target, NavigationOrigin.NewWindow);

    private NavigationDecision Evaluate(string target, NavigationOrigin origin) =>
        _policyEvaluator.Evaluate(new NavigationRequest(target, origin));
}
