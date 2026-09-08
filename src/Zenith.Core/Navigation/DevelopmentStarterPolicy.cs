namespace Zenith.Core.Navigation;

public static class DevelopmentStarterPolicy
{
    public static IReadOnlyList<StarterWhitelistSite> Sites { get; } =
        Array.AsReadOnly<StarterWhitelistSite>(
        [
            new("arXiv", "arxiv.org", "https://arxiv.org/"),
            new("arXiv Export", "export.arxiv.org", "https://export.arxiv.org/"),

            new("Google Scholar", "scholar.google.com", "https://scholar.google.com/"),
            new("Google Search", "google.com", "https://google.com/"),
            new("Google Drive", "drive.google.com", "https://drive.google.com/"),
            new("Google Docs", "docs.google.com", "https://docs.google.com/"),

            new("Semantic Scholar", "semanticscholar.org", "https://www.semanticscholar.org/"),
            new("Semantic Scholar API", "api.semanticscholar.org", "https://api.semanticscholar.org/"),

            new("ResearchGate", "researchgate.net", "https://www.researchgate.net/"),
            new("Academia", "academia.edu", "https://www.academia.edu/"),

            new("Nature", "nature.com", "https://www.nature.com/"),
            new("Science", "science.org", "https://www.science.org/"),
            new("Cell", "cell.com", "https://www.cell.com/"),
            new("Elsevier", "elsevier.com", "https://www.elsevier.com/"),
            new("Springer", "springer.com", "https://www.springer.com/"),
            new("SpringerLink", "link.springer.com", "https://link.springer.com/"),
            new("Wiley Online Library", "wiley.com", "https://www.wiley.com/"),
            new("IEEE Xplore", "ieeexplore.ieee.org", "https://ieeexplore.ieee.org/"),
            new("APS Journals", "aps.org", "https://www.aps.org/"),
            new("Physical Review", "journals.aps.org", "https://journals.aps.org/"),

            new("Crossref", "crossref.org", "https://www.crossref.org/"),
            new("DOI Resolver", "doi.org", "https://doi.org/"),

            new("Overleaf", "overleaf.com", "https://www.overleaf.com/"),
            new("Overleaf Editor", "sharelatex.com", "https://www.sharelatex.com/"),

            new("Zotero", "zotero.org", "https://www.zotero.org/"),

            new("GitHub", "github.com", "https://github.com/"),
            new("GitHub Raw", "raw.githubusercontent.com", "https://raw.githubusercontent.com/"),
            new("GitHub Pages", "github.io", "https://github.io/"),

            new("GitLab", "gitlab.com", "https://gitlab.com/"),

            new("Stack Overflow", "stackoverflow.com", "https://stackoverflow.com/"),
            new("Stack Exchange", "stackexchange.com", "https://stackexchange.com/"),
            new("Math Stack Exchange", "math.stackexchange.com", "https://math.stackexchange.com/"),
            new("Physics Stack Exchange", "physics.stackexchange.com", "https://physics.stackexchange.com/"),

            new("Wolfram Alpha", "wolframalpha.com", "https://www.wolframalpha.com/"),
            new("Wolfram Cloud", "wolframcloud.com", "https://www.wolframcloud.com/"),

            new("ChatGPT", "chatgpt.com", "https://chatgpt.com/"),
            new("OpenAI", "openai.com", "https://openai.com/"),
            new("Claude", "claude.ai", "https://claude.ai/"),

            new("Microsoft Learn", "learn.microsoft.com", "https://learn.microsoft.com/"),
            new("Microsoft Docs", "docs.microsoft.com", "https://docs.microsoft.com/"),

            new("MDN Web Docs", "developer.mozilla.org", "https://developer.mozilla.org/"),

            new("Wikipedia", "wikipedia.org", "https://wikipedia.org/"),
            new("Wikimedia Commons", "wikimedia.org", "https://wikimedia.org/"),

            new("Internet Archive", "archive.org", "https://archive.org/"),

            new("YouTube", "youtube.com", "https://youtube.com/"),
            new("YouTube NoCookie", "youtube-nocookie.com", "https://youtube-nocookie.com/"),

            new("Reddit", "reddit.com", "https://reddit.com/")
        ]);

    public static SitePolicySnapshot Snapshot { get; } = new(
        Sites.Select(site => new SitePolicyEntry(
            site.Host,
            AccessClass.Whitelist,
            site.IncludeSubdomains)));
}
