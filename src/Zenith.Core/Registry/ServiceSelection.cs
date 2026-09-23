namespace Zenith.Core.Registry;

/// <summary>Descriptive selection at a catalog revision. It is not an approved service grant.</summary>
public sealed record ServiceSelection
{
    public ServiceSelection(string serviceId, string registryRevision, IReadOnlyList<string>? optionalHostnames = null)
    {
        ServiceId = RegistryFields.Id(serviceId, nameof(ServiceId));
        RegistryRevision = RegistryFields.Text(registryRevision, nameof(RegistryRevision), 80);
        var hosts = RegistryFields.Copy(optionalHostnames ?? [], nameof(OptionalHostnames));
        if (hosts.Any(h => !Navigation.SiteIdentity.TryCreate(h, out var identity) || h != identity.Host) || hosts.Distinct().Count() != hosts.Count)
            throw new ArgumentException("Optional hostnames must be unique and normalized.");
        OptionalHostnames = Array.AsReadOnly(hosts.Order(StringComparer.Ordinal).ToArray());
    }

    public string ServiceId { get; }
    public string RegistryRevision { get; }
    public IReadOnlyList<string> OptionalHostnames { get; }
}
