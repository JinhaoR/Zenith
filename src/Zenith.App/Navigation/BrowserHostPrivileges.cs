using System.Security.Principal;

namespace Zenith.App.Navigation;

/// <summary>Deployment safety, independent of website and Vault policy.</summary>
internal static class BrowserHostPrivileges
{
    internal static bool Allows(bool isSystem, bool isAdministrator) => !isSystem && !isAdministrator;

    internal static bool CanStart()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return Allows(identity.IsSystem, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator));
        }
        catch (Exception)
        {
            // An unknown token must not start the browser with unknown privileges.
            return false;
        }
    }
}
