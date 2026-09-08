using Zenith.Core.Filtering;

namespace Zenith.Core.Tests.Navigation;

public sealed class ResourceFilteringPolicyTests
{
    [Theory]
    [InlineData("https://example.org/page", "https://EXAMPLE.org:443/page#part", ResourceKind.Document)]
    [InlineData("https://example.org/frame", "https://example.org/page", ResourceKind.Frame)]
    [InlineData("https://example.org/Page", "https://example.org/page", ResourceKind.Frame)]
    [InlineData("https://frame.example/", "https://example.org/page", ResourceKind.Frame)]
    [InlineData("https://example.org/", "about:blank", ResourceKind.Document)]
    public void DocumentContextPreservesTopLevelIdentity(string url, string main, ResourceKind expected) =>
        Assert.Equal(expected, ResourceDocumentContext.Kind(url, main));
    private sealed class Source(HostsBlacklist? list) : IBlacklistSource { public HostsBlacklist? Current => list; }
    private sealed class Engine(bool blocked) : IResourceFilterEngine
    {
        public int Calls { get; private set; }
        public bool Blocks(ResourceRequest request) { Calls++; return blocked; }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingOrMatchingBlacklistCannotBeOverriddenByAdExceptions(bool missing)
    {
        var engine = new Engine(false);
        var policy = new ResourceFilteringPolicy(new Source(missing ? null : HostsBlacklist.Parse("0.0.0.0 blocked.example")), engine);
        Assert.Equal(ResourceFilterDecision.Blacklist, policy.Evaluate(new("https://blocked.example/", "https://sphere.example/", ResourceKind.Frame)));
        Assert.Equal(0, engine.Calls);
    }
    [Fact]
    public void ResourceRulesDoNotClassifyMainDocuments()
    {
        var engine = new Engine(true);
        var policy = new ResourceFilteringPolicy(new Source(HostsBlacklist.Parse("0.0.0.0 blocked.example")), engine);
        Assert.Equal(ResourceFilterDecision.Allow, policy.Evaluate(new("https://sphere.example/", "", ResourceKind.Document)));
        Assert.Equal(0, engine.Calls);
        Assert.Equal(ResourceFilterDecision.Advertisement, policy.Evaluate(new("https://sphere.example/ad.js", "https://sphere.example/", ResourceKind.Script)));
        Assert.Equal(1, engine.Calls);
    }
}
