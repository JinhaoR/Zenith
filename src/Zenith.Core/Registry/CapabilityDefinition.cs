namespace Zenith.Core.Registry;

/// <summary>A service function, separate from native browser permissions.</summary>
public sealed record CapabilityDefinition
{
    public CapabilityDefinition(string id, string name, string description)
    {
        Id = RegistryFields.Id(id);
        Name = RegistryFields.Text(name, nameof(Name), 200);
        Description = RegistryFields.Text(description, nameof(Description));
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
}
