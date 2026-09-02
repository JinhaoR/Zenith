namespace Zenith.Core.Navigation;

public sealed class SitePolicyNavigationEvaluator : INavigationPolicyEvaluator
{
    private readonly ISitePolicySource _policySource;

    public SitePolicyNavigationEvaluator(ISitePolicySource policySource)
    {
        ArgumentNullException.ThrowIfNull(policySource);
        _policySource = policySource;
    }

    public NavigationDecision Evaluate(NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NavigationUriNormalizer.TryNormalize(request.Target, out var target))
        {
            return new NavigationDecision.Denied(NavigationDenialReason.UnsupportedTarget);
        }

        SitePolicySnapshot? policy;
        try
        {
            if (!_policySource.TryGetActivePolicy(out policy) || policy is null)
            {
                return new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
            }
        }
        catch (Exception)
        {
            return new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
        }

        return policy.Classify(target.Site) switch
        {
            AccessClass.Whitelist => new NavigationDecision.Allowed(target.Target),
            AccessClass.Blacklist =>
                new NavigationDecision.Denied(NavigationDenialReason.Blacklisted),
            AccessClass.Greylist =>
                new NavigationDecision.Denied(NavigationDenialReason.Greylisted),
            _ => new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable)
        };
    }
}
