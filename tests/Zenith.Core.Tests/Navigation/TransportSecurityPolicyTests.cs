using Zenith.Core.Navigation;
using Zenith.Core.Permissions;
using Zenith.Core.Access;

namespace Zenith.Core.Tests.Navigation;

public sealed class TransportSecurityPolicyTests
{
    [Theory]
    [InlineData("https://mail.example/", true)]
    [InlineData("wss://mail.example/socket", true)]
    [InlineData("http://mail.example/", false)]
    [InlineData("ws://mail.example/socket", false)]
    [InlineData("http://localhost/", false)]
    [InlineData("http://localhost.evil.example/", false)]
    [InlineData("http://127.0.0.1.evil.example/", false)]
    [InlineData("http://192.168.1.1/", false)]
    [InlineData("http://[fc00::1]/", false)]
    [InlineData("http://127.0.0.1:8000/", true)]
    [InlineData("http://127.0.0.2/", true)]
    [InlineData("http://[::1]:8000/", true)]
    [InlineData("https://user:password@mail.example/", false)]
    [InlineData("wss://user:password@mail.example/", false)]
    [InlineData("wss://mail.example/a\nb", false)]
    [InlineData("file:///C:/private", false)]
    [InlineData("not a URL", false)]
    public void TransportHasNoHostnameOrCredentialBypasses(string address, bool allowed) =>
        Assert.Equal(allowed, TransportSecurityPolicy.Allows(address));

    [Theory]
    [InlineData(NavigationOrigin.AddressBar)]
    [InlineData(NavigationOrigin.WebView)]
    [InlineData(NavigationOrigin.NewWindow)]
    public void WhitelistCannotAuthorizeUnencryptedPublicNavigation(NavigationOrigin origin)
    {
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("mail.example", AccessClass.Whitelist, true)])));
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.InsecureTransport),
            evaluator.Evaluate(new("http://mail.example/login", origin)));
    }

    private sealed class GrantSource : IAccessGrantSource
    {
        public bool TryGetGrant(SiteIdentity site, out AccessGrant? grant)
        {
            grant = new AccessGrant(site, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
            return true;
        }
    }

    [Fact]
    public void ValidGrantAndEmbeddedCompatibilityCannotOverrideTransport()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("sphere.example", AccessClass.Whitelist)])), new GrantSource());
        Assert.IsType<NavigationDecision.Allowed>(evaluator.Evaluate(new("https://mail.example/", NavigationOrigin.WebView)));
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.InsecureTransport),
            evaluator.Evaluate(new("http://mail.example/", NavigationOrigin.WebView)));
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.InsecureTransport),
            new DocumentRequestPolicy(evaluator).Evaluate("http://mail.example/frame", false, "https://sphere.example/"));
    }

    [Fact]
    public void BlacklistRetainsPrecedenceAndLoopbackStillNeedsAuthorization()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("mail.example", AccessClass.Blacklist)])));
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.Blacklisted),
            evaluator.Evaluate(new("http://mail.example/", NavigationOrigin.WebView)));
        Assert.Equal(new NavigationDecision.Denied(NavigationDenialReason.Greylisted),
            evaluator.Evaluate(new("http://127.0.0.1/", NavigationOrigin.WebView)));
    }

    [Theory]
    [InlineData("https://mail.example/", NetworkRequestSource.BackgroundWorker, false)]
    [InlineData("http://127.0.0.1/", NetworkRequestSource.BackgroundWorker, false)]
    [InlineData("https://mail.example/", (NetworkRequestSource)999, false)]
    [InlineData("https://mail.example/", NetworkRequestSource.Unknown, false)]
    [InlineData("https://mail.example/", NetworkRequestSource.Document, true)]
    [InlineData("http://mail.example/", NetworkRequestSource.Document, false)]
    public void WorkerOrUnknownSourceCannotBorrowPageAuthorization(string address, NetworkRequestSource source, bool allowed) =>
        Assert.Equal(allowed, new NetworkSafetyPolicy().Allows(address, source));
}
