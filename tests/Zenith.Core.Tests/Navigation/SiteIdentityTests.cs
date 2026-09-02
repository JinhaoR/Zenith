using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class SiteIdentityTests
{
    [Theory]
    [InlineData("GitHub.COM.", "github.com")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    public void TryCreateCanonicalizesValidHosts(string host, string expected)
    {
        var created = SiteIdentity.TryCreate(host, out var identity);

        Assert.True(created);
        Assert.NotNull(identity);
        Assert.Equal(expected, identity.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" github.com")]
    [InlineData("github.com/path")]
    [InlineData("*.github.com")]
    public void TryCreateRejectsInvalidHosts(string host)
    {
        Assert.False(SiteIdentity.TryCreate(host, out var identity));
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("www.github.com", true)]
    [InlineData("docs.www.github.com", true)]
    [InlineData("notgithub.com", false)]
    [InlineData("github.com.evil.example", false)]
    public void IsSameOrSubdomainOfUsesLabelBoundaries(string candidate, bool expected)
    {
        Assert.True(SiteIdentity.TryCreate("github.com", out var parent));
        Assert.True(SiteIdentity.TryCreate(candidate, out var site));

        Assert.Equal(expected, site.IsSameOrSubdomainOf(parent));
    }
}
