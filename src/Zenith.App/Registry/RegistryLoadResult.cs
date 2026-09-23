using Zenith.Core.Registry;

namespace Zenith.App.Registry;

internal sealed class RegistryLoadResult
{
    private RegistryLoadResult(ServiceRegistry? registry, IEnumerable<RegistryValidationIssue> issues)
    {
        Registry = registry;
        Issues = Array.AsReadOnly(issues.ToArray());
    }

    public ServiceRegistry? Registry { get; }
    public bool IsSuccess => Registry is not null;
    public IReadOnlyList<RegistryValidationIssue> Issues { get; }
    internal static RegistryLoadResult Success(ServiceRegistry registry) => new(registry, []);
    internal static RegistryLoadResult Failure(IEnumerable<RegistryValidationIssue> issues) => new(null, issues);
}
