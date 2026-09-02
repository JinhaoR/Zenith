namespace Zenith.Core.Navigation;

public sealed class StarterWhitelistNavigationPolicyEvaluator : INavigationPolicyEvaluator
{
    public static IReadOnlyList<StarterWhitelistSite> StarterSites { get; } =
    [
        new("GitHub", "github.com", "https://github.com/"),
        new("ChatGPT", "chatgpt.com", "https://chatgpt.com/"),
        new("OpenAI", "openai.com", "https://openai.com/"),
        new("YouTube", "youtube.com", "https://youtube.com/"),
        new("Wikipedia", "wikipedia.org", "https://wikipedia.org/"),
        new("Reddit", "reddit.com", "https://reddit.com/"),
        new("Microsoft Learn", "learn.microsoft.com", "https://learn.microsoft.com/"),
        new("Google", "google.com", "https://google.com/"),
        new("Stack Overflow", "stackoverflow.com", "https://stackoverflow.com/"),
        new("GitLab", "gitlab.com", "https://gitlab.com/"),
        new("MDN Web Docs", "developer.mozilla.org", "https://developer.mozilla.org/"),
        new("Internet Archive", "archive.org", "https://archive.org/")
    ];

    private readonly IReadOnlyList<StarterWhitelistSite> _sites;

    public StarterWhitelistNavigationPolicyEvaluator(
        IReadOnlyList<StarterWhitelistSite>? sites = null)
    {
        _sites = sites ?? StarterSites;
    }

    public NavigationDecision Evaluate(NavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!NavigationUriNormalizer.TryNormalize(request.Target, out var target))
        {
            return new NavigationDecision.Denied(NavigationDenialReason.NotWhitelisted);
        }

        var allowed = _sites.Any(site =>
            target.Site == site.Identity ||
            (site.IncludeSubdomains && target.Site.IsSameOrSubdomainOf(site.Identity)));

        return allowed
            ? new NavigationDecision.Allowed(target.Target)
            : new NavigationDecision.Denied(NavigationDenialReason.NotWhitelisted);
    }
}
