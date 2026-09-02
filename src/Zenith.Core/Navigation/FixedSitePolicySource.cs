using System.Diagnostics.CodeAnalysis;

namespace Zenith.Core.Navigation;

public sealed class FixedSitePolicySource : ISitePolicySource
{
    private readonly SitePolicySnapshot _policy;

    public FixedSitePolicySource(SitePolicySnapshot policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public bool TryGetActivePolicy(
        [NotNullWhen(true)] out SitePolicySnapshot? policy)
    {
        policy = _policy;
        return true;
    }
}
