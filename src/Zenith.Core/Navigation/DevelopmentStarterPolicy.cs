namespace Zenith.Core.Navigation;

public static class DevelopmentStarterPolicy
{
    public static IReadOnlyList<StarterWhitelistSite> Sites { get; } =
        Array.AsReadOnly<StarterWhitelistSite>(
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
        ]);

    public static SitePolicySnapshot Snapshot { get; } = new(
        Sites.Select(site => new SitePolicyEntry(
            site.Host,
            AccessClass.Whitelist,
            site.IncludeSubdomains)));
}
