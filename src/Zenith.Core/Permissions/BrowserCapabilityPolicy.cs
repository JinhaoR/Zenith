namespace Zenith.Core.Permissions;

public enum BrowserCapability
{
    PermissionRequest,
    Download,
    ExternalApplication,
    InvalidServerCertificate,
    ClientCertificate,
    HttpAuthentication
}

public sealed record CapabilityDecision(bool Allowed, string Explanation);

// No capability grants exist yet. Navigation access must never imply one.
// Phase 6 can introduce explicit Vault-backed grants at this boundary.
public sealed class BrowserCapabilityPolicy
{
    public CapabilityDecision Evaluate(BrowserCapability capability) => new(false, capability switch
    {
        BrowserCapability.Download => "Downloads are not enabled in Zenith yet. No file was saved.",
        BrowserCapability.ExternalApplication => "Opening external applications is not enabled in Zenith.",
        BrowserCapability.PermissionRequest => "Website permissions are not enabled in Zenith yet.",
        BrowserCapability.InvalidServerCertificate => "The connection's certificate could not be verified. Zenith did not allow a certificate exception.",
        BrowserCapability.ClientCertificate => "Signing in with a device certificate is not enabled in Zenith.",
        BrowserCapability.HttpAuthentication => "Browser-level authentication is not enabled in Zenith. Website sign-in forms are separate.",
        _ => "This website capability is not permitted."
    });
}
