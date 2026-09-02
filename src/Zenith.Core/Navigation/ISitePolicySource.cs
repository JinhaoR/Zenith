using System.Diagnostics.CodeAnalysis;

namespace Zenith.Core.Navigation;

public interface ISitePolicySource
{
    bool TryGetActivePolicy(
        [NotNullWhen(true)] out SitePolicySnapshot? policy);
}
