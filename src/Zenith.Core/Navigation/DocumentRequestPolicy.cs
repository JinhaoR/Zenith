namespace Zenith.Core.Navigation;

/// <summary>Applies document policy without granting embedded sites durable access.</summary>
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
            // These embedded documents have no destination hostname. Native frame
            // events expose them whereas network interception did not. Preserve
            // inherited-document functionality only beneath current authorization;
            // Chromium still determines the actual inherited or opaque origin.
            if (string.Equals(target, "about:blank", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, "about:srcdoc", StringComparison.OrdinalIgnoreCase))
                return new NavigationDecision.Allowed(new Uri(target), permittedParent.AccessClass);
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
