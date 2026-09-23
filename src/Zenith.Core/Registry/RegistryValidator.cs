namespace Zenith.Core.Registry;

public static class RegistryValidator
{
    public static IReadOnlyList<RegistryValidationIssue> Validate(
        IReadOnlyList<ServiceDefinition> services, IReadOnlyList<CapabilityDefinition> capabilities,
        IReadOnlyList<InfrastructureDefinition> infrastructure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(infrastructure);
        var issues = new List<RegistryValidationIssue>();
        if (services.Any(s => s is null) || capabilities.Any(c => c is null) || infrastructure.Any(i => i is null))
            return [new("registry", "Definitions cannot be null.")];
        CheckIds(services.Select(s => s.Id), "services");
        CheckIds(capabilities.Select(c => c.Id), "capabilities");
        CheckIds(infrastructure.Select(i => i.Id), "infrastructure");
        var capabilityIds = capabilities.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var infrastructureIds = infrastructure.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var service in services)
        {
            CheckDomains(service.Domains, $"services/{service.Id}");
            foreach (var id in service.Capabilities.Where(id => !capabilityIds.Contains(id)))
                issues.Add(new($"services/{service.Id}/capabilities", $"Unknown capability: {id}."));
            foreach (var id in service.InfrastructureDependencies.Select(d => d.InfrastructureId).Where(id => !infrastructureIds.Contains(id)))
                issues.Add(new($"services/{service.Id}/requires", $"Unknown infrastructure: {id}."));
        }
        foreach (var item in infrastructure) CheckDomains(item.Domains, $"infrastructure/{item.Id}");
        return issues.AsReadOnly();

        void CheckIds(IEnumerable<string> ids, string location)
        {
            foreach (var group in ids.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                issues.Add(new(location, $"Duplicate ID: {group.Key}."));
        }

        void CheckDomains(IReadOnlyList<DomainRequirement> domains, string location)
        {
            if (domains.Count == 0) issues.Add(new(location, "At least one domain requirement is needed."));
            foreach (var group in domains.GroupBy(d => d.Hostname, StringComparer.Ordinal).Where(g => g.Count() > 1))
                issues.Add(new(location, $"Duplicate domain: {group.Key}."));
        }
    }
}
