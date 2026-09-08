using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class WebsiteIdentityTests
{
    [Theory]
    [InlineData("https://EXAMPLE.com/path?token=secret#password", "https://example.com", true)]
    [InlineData("http://example.com:8080/login", "http://example.com:8080", false)]
    [InlineData("https://bücher.de/", "https://xn--bcher-kva.de", true)]
    [InlineData("https://example.com.evil.test", "https://example.com.evil.test", true)]
    public void NativeIdentityShowsCanonicalOriginWithoutSensitiveUrlComponents(string input, string origin, bool https)
    {
        Assert.Equal(new WebsiteIdentity(origin, https), WebsiteIdentity.FromAddress(input));
    }

    [Theory]
    [InlineData("https://user:password@example.com")]
    [InlineData("about:blank")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://example.com\n")]
    [InlineData(null)]
    public void InvalidOrNonWebsiteAddressesHaveNoWebsiteIdentity(string? input) =>
        Assert.Null(WebsiteIdentity.FromAddress(input));
}
