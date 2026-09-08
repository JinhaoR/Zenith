using Zenith.Core.Access;

namespace Zenith.Core.Navigation;

public sealed class SitePolicyNavigationEvaluator : INavigationPolicyEvaluator
{
    private readonly ISitePolicySource _policySource;
    private readonly IAccessGrantSource? _grantSource;
    private readonly TimeProvider _timeProvider;

    public SitePolicyNavigationEvaluator(ISitePolicySource policySource,
        IAccessGrantSource? grantSource = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(policySource);
        _policySource = policySource;
        _grantSource = grantSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            AccessClass.Whitelist => new NavigationDecision.Allowed(target.Target, AccessClass.Whitelist),
            AccessClass.Blacklist =>
                new NavigationDecision.Denied(NavigationDenialReason.Blacklisted),
            AccessClass.Greylist => EvaluateGrant(target),
            _ => new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable)
        };
    }

    private NavigationDecision EvaluateGrant(NormalizedNavigationTarget target)
    {
        try
        {
            if (_grantSource is not null &&
                _grantSource.TryGetGrant(target.Site, out var grant) &&
                grant is not null && grant.Covers(target.Site, _timeProvider.GetUtcNow()))
            {
                return new NavigationDecision.Allowed(target.Target, AccessClass.Greylist);
            }
        }
        catch (Exception)
        {
            return new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
        }

        return new NavigationDecision.Denied(NavigationDenialReason.Greylisted);
    }
}
