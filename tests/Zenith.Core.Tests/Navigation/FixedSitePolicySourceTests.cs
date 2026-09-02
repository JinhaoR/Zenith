using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Navigation;

public sealed class FixedSitePolicySourceTests
{
    [Fact]
    public void SourceReturnsItsValidatedSnapshot()
    {
        var expected = new SitePolicySnapshot([], revision: 3);
        var source = new FixedSitePolicySource(expected);

        var available = source.TryGetActivePolicy(out var actual);

        Assert.True(available);
        Assert.Same(expected, actual);
    }

    [Fact]
    public void ConstructorRejectsAMissingSnapshot()
    {
        Assert.Throws<ArgumentNullException>(() => new FixedSitePolicySource(null!));
    }
}
