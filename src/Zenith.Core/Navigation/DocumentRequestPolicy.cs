namespace Zenith.Core.Navigation;

/// <summary>Applies navigation policy to network documents without granting embedded sites durable access.</summary>
public sealed class DocumentRequestPolicy(INavigationPolicyEvaluator evaluator)
{
    public NavigationDecision Evaluate(string target, bool isMainFrame, string? topLevelTarget)
    {
        try
        {
            var destination = evaluator.Evaluate(new(target, NavigationOrigin.WebView));
            if (isMainFrame) return destination;
            if (string.IsNullOrEmpty(topLevelTarget)) return Unavailable();
            var parent = evaluator.Evaluate(new(topLevelTarget, NavigationOrigin.WebView));
            if (parent is not NavigationDecision.Allowed permittedParent) return parent;
            if (destination is NavigationDecision.Allowed) return destination;
            if (permittedParent.AccessClass == AccessClass.Whitelist &&
                destination is NavigationDecision.Denied { Reason: NavigationDenialReason.Greylisted } &&
                NavigationUriNormalizer.TryNormalize(target, out var normalized))
                return new NavigationDecision.Allowed(normalized.Target, AccessClass.Greylist);
            return destination;
        }
        catch (Exception) { return Unavailable(); }
    }

    private static NavigationDecision Unavailable() => new NavigationDecision.Denied(NavigationDenialReason.PolicyUnavailable);
}
