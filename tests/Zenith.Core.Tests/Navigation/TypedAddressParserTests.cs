using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class TypedAddressParserTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("EXAMPLE.com/path", "https://example.com/path")]
    [InlineData(" www.example.com/path?q=1#part ", "https://www.example.com/path?q=1#part")]
    [InlineData("example.com:8443/path", "https://example.com:8443/path")]
    [InlineData("http://example.com/path", "http://example.com/path")]
    [InlineData("127.0.0.1:8080", "https://127.0.0.1:8080/")]
    [InlineData("[::1]:8080", "https://[::1]:8080/")]
    [InlineData("localhost:8080", "https://localhost:8080/")]
    public void ExpandsTypedAddressesWithoutAddingWww(string input, string expected)
    {
        Assert.True(TypedAddressParser.TryParse(input, out var target));
        Assert.Equal(expected, target.Target.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("search words")]
    [InlineData("github")]
    [InlineData("/example.com")]
    [InlineData("//example.com")]
    [InlineData("user@example.com")]
    [InlineData("https://user:password@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("about:blank")]
    [InlineData("file:///C:/test")]
    [InlineData("ftp://example.com")]
    [InlineData("https:\\example.com")]
    [InlineData("example.com\n")]
    [InlineData("example.com:bad")]
    public void RejectsUnsupportedOrAmbiguousInput(string? input)
    {
        Assert.False(TypedAddressParser.TryParse(input, out var target));
        Assert.Null(target);
    }

    [Fact]
    public void ExpansionDoesNotRelaxPolicyOrEngineParsing()
    {
        Assert.True(TypedAddressParser.TryParse("blocked.example", out var target));
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("blocked.example", AccessClass.Blacklist)])));
        Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(new(target.Target.AbsoluteUri, NavigationOrigin.AddressBar)));
        Assert.False(NavigationUriNormalizer.TryNormalize("example.com", out _));
    }

    [Fact]
    public void BareHostDoesNotInheritWwwOnlyWhitelist()
    {
        var evaluator = new SitePolicyNavigationEvaluator(new FixedSitePolicySource(new SitePolicySnapshot(
            [new SitePolicyEntry("www.example.com", AccessClass.Whitelist)])));
        Assert.True(TypedAddressParser.TryParse("example.com", out var target));
        Assert.IsType<NavigationDecision.Denied>(evaluator.Evaluate(new(target.Target.AbsoluteUri, NavigationOrigin.AddressBar)));
    }
}
