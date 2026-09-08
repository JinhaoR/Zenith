using Zenith.Core.Navigation;

namespace Zenith.Core.Access;

public interface IAccessGrantSource
{
    bool TryGetGrant(SiteIdentity site, out AccessGrant? grant);
}
