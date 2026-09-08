using Zenith.Core.Navigation;

namespace Zenith.Core.Filtering;

public enum ResourceKind { Document, Frame, Script, Stylesheet, Image, Font, Media, Fetch, Ping, WebSocket, Other }
public enum ResourceFilterDecision { Allow, Blacklist, Advertisement }
public sealed record ResourceRequest(string Url, string SourceUrl, ResourceKind Kind);

public static class ResourceDocumentContext
{
    // WebView2 exposes Document for both page and frame requests, without a frame ID.
    // Compare against the native top-level navigation target, ignoring fragments.
    public static ResourceKind Kind(string requestUrl, string mainNavigationUrl)
    {
        if (!NavigationUriNormalizer.TryNormalize(requestUrl, out var request) ||
            !NavigationUriNormalizer.TryNormalize(mainNavigationUrl, out var main)) return ResourceKind.Document;
        return Uri.Compare(request.Target, main.Target, UriComponents.HttpRequestUrl, UriFormat.UriEscaped, StringComparison.Ordinal) == 0
            ? ResourceKind.Document : ResourceKind.Frame;
    }
}

public interface IResourceFilterEngine
{
    bool Blocks(ResourceRequest request);
}

/// <summary>Resource-list exceptions have no authority over site classification.</summary>
public sealed class ResourceFilteringPolicy(IBlacklistSource blacklist, IResourceFilterEngine? advertisements = null)
{
    public ResourceFilterDecision Evaluate(ResourceRequest request)
    {
        if (BlacklistRequestPolicy.IsBlocked(blacklist.Current, request.Url))
            return ResourceFilterDecision.Blacklist;
        // Navigation policy owns main documents. Resource rules never reclassify sites.
        if (request.Kind != ResourceKind.Document && advertisements?.Blocks(request) == true)
            return ResourceFilterDecision.Advertisement;
        return ResourceFilterDecision.Allow;
    }
}
