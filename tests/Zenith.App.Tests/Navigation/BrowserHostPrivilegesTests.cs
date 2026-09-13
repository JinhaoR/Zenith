using Zenith.App.Navigation;

namespace Zenith.App.Tests.Navigation;

public sealed class BrowserHostPrivilegesTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void OnlyStandardUsersCanHostBrowser(bool system, bool administrator, bool expected) =>
        Assert.Equal(expected, BrowserHostPrivileges.Allows(system, administrator));
}
