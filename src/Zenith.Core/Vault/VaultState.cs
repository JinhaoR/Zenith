using Zenith.Core.Access;
using Zenith.Core.Navigation;

namespace Zenith.Core.Vault;

public sealed record VaultSettings(int GreylistSeconds = 5, int GrantSeconds = 5, int VaultSeconds = 5)
{
    public void Validate()
    {
        if (GreylistSeconds is < 5 or > 86400 || GrantSeconds is < 5 or > 86400 || VaultSeconds is < 5 or > 2592000)
            throw new ArgumentException("Greylist wait and visits must be 5 seconds–24 hours; Vault wait must be 5 seconds–30 days.");
    }
}

public sealed record VaultSite(string Host, AccessClass AccessClass, bool IncludeSubdomains, string? DisplayName = null);
public sealed record VaultSiteAddition(string Host, bool IncludeSubdomains = false, string? DisplayName = null);
public sealed record VaultEdit(int? GreylistSeconds = null, int? GrantSeconds = null, int? VaultSeconds = null,
    string? AddHost = null, bool IncludeSubdomains = false, bool ChangePassword = false, string? RemoveHost = null,
    IReadOnlyList<VaultSiteAddition>? AddSites = null, IReadOnlyList<string>? RemoveSites = null)
{
    public IEnumerable<VaultSiteAddition> Additions() => AddSites ??
        (string.IsNullOrWhiteSpace(AddHost) ? [] : [new VaultSiteAddition(AddHost, IncludeSubdomains)]);
    public IEnumerable<string> Removals() => RemoveSites ??
        (string.IsNullOrWhiteSpace(RemoveHost) ? [] : [RemoveHost]);
}
public sealed record PendingPolicyChange(Guid Id, long BaseRevision, DateTimeOffset ProposedAt,
    DateTimeOffset EligibleAt, VaultEdit Edit, string? PasswordVerifier);
public sealed record VaultState(long Revision, VaultSettings Settings, VaultSite[] Sites,
    DateTimeOffset LastObservedUtc, DateTimeOffset RetryAfter, PendingPolicyChange? Pending = null)
{
    public static VaultState CreateDevelopment(DateTimeOffset now) => new(0, new(),
        DevelopmentStarterPolicy.Snapshot.Entries.Select(entry =>
            new VaultSite(entry.Identity.Host, entry.AccessClass, entry.IncludeSubdomains)).ToArray(), now, DateTimeOffset.MinValue);

    public SitePolicySnapshot ToPolicy() => new(Sites.Select(site => new SitePolicyEntry(site.Host, site.AccessClass, site.IncludeSubdomains, site.DisplayName)), Revision);

    public IReadOnlyList<VaultSite> GetIndependentWhitelistScopes()
    {
        var whitelist = Sites.Where(site => site.AccessClass == AccessClass.Whitelist).ToArray();
        return whitelist.Where(site =>
        {
            if (!SiteIdentity.TryCreate(site.Host, out var identity)) return false;
            return !whitelist.Any(parent =>
                parent != site &&
                parent.IncludeSubdomains &&
                SiteIdentity.TryCreate(parent.Host, out var parentIdentity) &&
                identity.IsSameOrSubdomainOf(parentIdentity));
        }).OrderBy(site => site.Host, StringComparer.Ordinal).ToArray();
    }

    public void Validate()
    {
        if (Revision < 0 || Settings is null || Sites is null || Sites.Length > 1000 ||
            LastObservedUtc < DateTimeOffset.UnixEpoch || RetryAfter > LastObservedUtc.AddSeconds(5))
            throw new InvalidOperationException("Invalid Vault state.");
        Settings.Validate();
        _ = ToPolicy();
        if (Sites.Any(site => !SiteIdentity.TryCreate(site.Host, out var identity) || identity.Host != site.Host))
            throw new InvalidOperationException("Vault hostnames must be canonical.");
        if (Sites.Any(site => site.DisplayName is { } name &&
            (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl))))
            throw new InvalidOperationException("Invalid site display name.");
        if (Sites.Select(site => (site.Host, site.AccessClass)).Distinct().Count() != Sites.Length)
            throw new InvalidOperationException("Duplicate Vault entries.");
        if (Pending is { } pending)
        {
            if (pending.Id == Guid.Empty || pending.BaseRevision != Revision || pending.Edit is null ||
                pending.ProposedAt < DateTimeOffset.UnixEpoch || pending.ProposedAt > LastObservedUtc ||
                pending.EligibleAt - pending.ProposedAt != TimeSpan.FromSeconds(Settings.VaultSeconds) ||
                pending.Edit.ChangePassword != (pending.PasswordVerifier is { Length: > 0 }))
                throw new InvalidOperationException("Invalid pending policy change.");
            _ = VaultProposalRules.Normalize(this, pending.Edit);
        }
    }
}

public enum VaultPhase { SetupRequired, Ready, Waiting, Eligible, Unavailable, ClockInvalid }
public enum VaultResult { Staged, Applied, Cancelled, WrongPassword, RetryLater, TooEarly, Stale, Invalid, Unavailable }
public sealed record VaultStatus(VaultPhase Phase, VaultState? State = null, DateTimeOffset? Now = null);
public sealed record VaultOutcome(VaultResult Result, string Message);
public sealed record VaultReview(VaultEdit Edit, VaultSettings ActiveSettings, long Revision);
public sealed record AccessTiming(int CooldownSeconds, int GrantSeconds, long Revision);
public interface IAccessRulesSource { AccessTiming GetAccessTiming(); }

public interface IVaultStore : IAccessStateStore, IAccessAuthenticator
{
    VaultState LoadVault();
    // Called within SyncRoot, together with authentication and revision validation.
    void SaveVault(VaultState state, string? replacementVerifier = null);
    string PreparePassword(string password);
}
