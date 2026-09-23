using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zenith.Core.Registry;

/// <summary>Versioned, length-prefixed content identities. These hashes are not signatures or authority.</summary>
public static class RegistryContentIdentity
{
    public static bool IsFingerprint(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Registry(ServiceRegistry registry) => Hash(w =>
    {
        w.Write("zenith-registry-content-1");
        w.Write(registry.Revision);
        Items(w, registry.Capabilities.OrderBy(c => c.Id, StringComparer.Ordinal), c => { w.Write(c.Id); w.Write(c.Name); w.Write(c.Description); });
        Items(w, registry.Infrastructure.OrderBy(i => i.Id, StringComparer.Ordinal), i =>
        {
            w.Write(i.Id); w.Write(i.Name); w.Write(i.Description);
            Items(w, i.Domains.OrderBy(d => d.Hostname, StringComparer.Ordinal), d => Domain(w, d));
        });
        Items(w, registry.Services.OrderBy(s => s.Id, StringComparer.Ordinal), s =>
        {
            w.Write(s.Id); w.Write(s.Name); w.Write(s.Category); w.Write(s.Description); w.Write((int)s.Status);
            Items(w, s.Capabilities.Order(StringComparer.Ordinal), w.Write);
            Items(w, s.Ecosystems.Order(StringComparer.Ordinal), w.Write);
            Items(w, s.Domains.OrderBy(d => d.Hostname, StringComparer.Ordinal), d => Domain(w, d));
            Items(w, s.InfrastructureDependencies.OrderBy(i => i.InfrastructureId, StringComparer.Ordinal), i =>
            { w.Write(i.InfrastructureId); Evidence(w, i.Provenance); });
        });
    });

    internal static string Proposal(AccessProposal p) => Hash(w =>
    {
        w.Write("zenith-service-proposal-1"); w.Write(p.SelectedService.ServiceId); w.Write(p.ServiceName!);
        w.Write(p.RegistryRevision); w.Write(p.RegistryFingerprint!); w.Write(p.PolicyRevision!.Value);
        Items(w, p.SelectedService.OptionalHostnames, w.Write);
        Items(w, p.ProposedDomains, d => Candidate(w, d));
        Items(w, p.OptionalDomains, d => Candidate(w, d));
        Items(w, p.Conflicts, w.Write); Items(w, p.Warnings, w.Write);
    });

    internal static string InfrastructureProposal(InfrastructureBaselineProposal p) => Hash(w =>
    {
        w.Write("zenith-infrastructure-proposal-1"); w.Write(p.RegistryRevision); w.Write(p.RegistryFingerprint); w.Write(p.PolicyRevision);
        Items(w, p.Endpoints, e => { w.Write(e.InfrastructureId); w.Write(e.Name); Domain(w, e.Requirement); });
        Items(w, p.NewHostnames, w.Write);
    });

    internal static string LocalExtensionProposal(LocalServiceExtensionProposal p) => Hash(w =>
    {
        w.Write("zenith-local-extension-1"); w.Write(p.ServiceId); w.Write(p.ServiceName);
        w.Write(p.ServiceEntryPointHostname);
        w.Write(p.Hostname); w.Write(p.Purpose); w.Write(p.PolicyRevision);
    });

    private static void Candidate(BinaryWriter w, ProposedDomain d)
    {
        w.Write(d.Hostname); w.Write((int)d.AccessState); Items(w, d.Reasons, w.Write);
        Items(w, d.ExistingScopes, s => { w.Write(s.Hostname); w.Write(s.IncludeSubdomains); });
        Items(w, d.Relationships, r => { w.Write(r.ServiceId); Text(w, r.InfrastructureId); Domain(w, r.Requirement); Evidence(w, r.InfrastructureProvenance); });
    }
    private static void Domain(BinaryWriter w, DomainRequirement d)
    {
        w.Write(d.Hostname); w.Write(d.Purpose); w.Write(d.Required); w.Write((int)d.DependencyType);
        w.Write((int)d.PermissionApplicability); Evidence(w, d.Provenance);
    }
    private static void Evidence(BinaryWriter w, RelationshipProvenance? p)
    {
        w.Write(p is not null); if (p is null) return;
        w.Write((int)p.SourceType); Text(w, p.SourceReference); Text(w, p.Explanation);
        w.Write(p.Review is not null);
        if (p.Review is { } r) { w.Write(r.ReviewedBy); w.Write(r.ReviewedAt.ToString("O", CultureInfo.InvariantCulture)); }
    }
    private static void Text(BinaryWriter w, string? text) { w.Write(text is not null); if (text is not null) w.Write(text); }
    private static void Items<T>(BinaryWriter w, IEnumerable<T> items, Action<T> write)
    { var values = items.ToArray(); w.Write(values.Length); foreach (var item in values) write(item); }
    private static string Hash(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) write(writer);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }
}
