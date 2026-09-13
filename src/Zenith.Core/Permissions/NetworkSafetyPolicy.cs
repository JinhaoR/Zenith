using Zenith.Core.Navigation;

namespace Zenith.Core.Permissions;

public enum NetworkRequestSource { Unknown, Document, BackgroundWorker }

/// <summary>Background capabilities and transport do not inherit page authorization.</summary>
public sealed class NetworkSafetyPolicy
{
    public bool Allows(string address, NetworkRequestSource source) => source switch
    {
        NetworkRequestSource.Document => TransportSecurityPolicy.Allows(address),
        NetworkRequestSource.BackgroundWorker => false,
        _ => false
    };
}
