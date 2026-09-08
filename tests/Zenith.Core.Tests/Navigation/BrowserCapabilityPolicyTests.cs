using Zenith.Core.Permissions;

namespace Zenith.Core.Tests.Navigation;

public sealed class BrowserCapabilityPolicyTests
{
    [Theory]
    [InlineData(BrowserCapability.PermissionRequest)]
    [InlineData(BrowserCapability.Download)]
    [InlineData(BrowserCapability.ExternalApplication)]
    [InlineData((BrowserCapability)999)]
    public void CapabilitiesWithoutExplicitGrantsAreDenied(BrowserCapability capability)
    {
        var decision = new BrowserCapabilityPolicy().Evaluate(capability);
        Assert.False(decision.Allowed);
        Assert.False(string.IsNullOrWhiteSpace(decision.Explanation));
    }
}
