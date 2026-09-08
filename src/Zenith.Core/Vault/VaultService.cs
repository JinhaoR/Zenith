using System.Diagnostics.CodeAnalysis;
using Zenith.Core.Access;
using Zenith.Core.Navigation;

namespace Zenith.Core.Vault;

public sealed class VaultService : ISitePolicySource, IAccessRulesSource
{
    private readonly IVaultStore _store;
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _anchorUtc;
    private readonly long _anchorTimestamp;
    private bool _clockInvalid;
    private readonly Zenith.Core.Filtering.IBlacklistSource? _blacklist;

    public VaultService(IVaultStore store, TimeProvider? clock = null, Zenith.Core.Filtering.IBlacklistSource? blacklist = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
        _blacklist = blacklist;
        _anchorUtc = _clock.GetUtcNow();
        _anchorTimestamp = _clock.GetTimestamp();
    }

    public bool TryGetActivePolicy([NotNullWhen(true)] out SitePolicySnapshot? snapshot)
    {
        lock (_store.SyncRoot)
        {
            try
            {
                var policy = ReadState().ToPolicy();
                var blacklist = _blacklist is null ? null : _blacklist.Current ?? throw new InvalidOperationException("Blacklist unavailable.");
                snapshot = new(policy.Entries, policy.Revision, blacklist);
                return true;
            }
            catch (Exception) { snapshot = null; return false; }
        }
    }

    public AccessTiming GetAccessTiming()
    {
        lock (_store.SyncRoot)
        {
            var state = ReadState();
            return new(state.Settings.GreylistSeconds, state.Settings.GrantSeconds, state.Revision);
        }
    }

    public AccessTiming? GetAccessTimingSafely()
    {
        try { return GetAccessTiming(); }
        catch (Exception) { return null; }
    }

    public VaultStatus GetStatus()
    {
        lock (_store.SyncRoot)
        {
            try
            {
                var state = ReadState();
                if (_store.ConfigurationState == AccessConfigurationState.NeedsSetup)
                    return new(VaultPhase.SetupRequired, state);
                var now = Observe(state);
                if (_clockInvalid) return new(VaultPhase.ClockInvalid);
                if (now - state.LastObservedUtc >= TimeSpan.FromSeconds(10))
                {
                    state = state with { LastObservedUtc = now };
                    _store.SaveVault(state);
                }
                return new(state.Pending is null ? VaultPhase.Ready :
                    now < state.Pending.EligibleAt ? VaultPhase.Waiting : VaultPhase.Eligible, state, now);
            }
            catch (Exception) { return new(VaultPhase.Unavailable); }
        }
    }

    public VaultReview Review(VaultEdit edit)
    {
        lock (_store.SyncRoot)
        {
            var state = ReadState();
            var normalized = VaultProposalRules.Normalize(state, edit);
            ValidateMandatoryBlacklist(normalized);
            return new(normalized, state.Settings, state.Revision);
        }
    }

    public VaultOutcome Stage(VaultEdit edit, string currentPassword, string? newPassword = null,
        long? expectedRevision = null)
    {
        lock (_store.SyncRoot)
        {
            try
            {
                var state = ReadState();
                if (expectedRevision is { } revision && state.Revision != revision)
                {
                    return new(VaultResult.Stale, "Active rules changed. Review your proposal again before starting its wait.");
                }
                edit = VaultProposalRules.Normalize(state, edit);
                ValidateMandatoryBlacklist(edit);
                if (edit.ChangePassword && (newPassword is null || newPassword.Length is < 15 or > 128))
                    return new(VaultResult.Invalid, "Use a new password of 15–128 characters.");
                var auth = Authenticate(state, currentPassword);
                if (auth is not null) return auth;
                var verifier = edit.ChangePassword ? _store.PreparePassword(newPassword!) : null;
                var now = Observe(state);
                if (_clockInvalid) return Unavailable();
                state = state with { LastObservedUtc = now, RetryAfter = DateTimeOffset.MinValue,
                    Pending = new(Guid.NewGuid(), state.Revision, now, now.AddSeconds(state.Settings.VaultSeconds), edit, verifier) };
                _store.SaveVault(state);
                return new(VaultResult.Staged, "Proposal saved. Current rules remain active until you return and confirm.");
            }
            catch (ArgumentException exception) { return new(VaultResult.Invalid, exception.Message); }
            catch (Exception) { return Unavailable(); }
        }
    }

    public VaultOutcome Confirm(Guid proposalId, string currentPassword)
    {
        lock (_store.SyncRoot)
        {
            try
            {
                var state = ReadState();
                if (state.Pending is not { } pending || pending.Id != proposalId || pending.BaseRevision != state.Revision)
                    return new(VaultResult.Stale, "This proposal changed. Review the current proposal before confirming.");
                var now = Observe(state);
                if (_clockInvalid) return Unavailable();
                if (now < pending.EligibleAt) return new(VaultResult.TooEarly, "The existing Vault waiting period has not finished.");
                var auth = Authenticate(state, currentPassword);
                if (auth is not null) return auth;
                now = Observe(state);
                if (_clockInvalid || now < pending.EligibleAt) return Unavailable();
                var edit = VaultProposalRules.Normalize(state, pending.Edit);
                ValidateMandatoryBlacklist(edit);
                var sites = VaultProposalRules.ApplySites(state, edit);
                var next = state with { Revision = checked(state.Revision + 1), Settings = VaultProposalRules.ApplySettings(state.Settings, edit),
                    Sites = sites, Pending = null, LastObservedUtc = now, RetryAfter = DateTimeOffset.MinValue };
                _store.SaveVault(next, pending.PasswordVerifier);
                return new(VaultResult.Applied, "Changes applied. Your new rules are now active.");
            }
            catch (Exception) { return Unavailable(); }
        }
    }

    public VaultOutcome Cancel(Guid proposalId)
    {
        lock (_store.SyncRoot)
        {
            try
            {
                var state = ReadState();
                if (state.Pending?.Id != proposalId) return new(VaultResult.Stale, "This proposal is no longer pending.");
                _store.SaveVault(state with { Pending = null });
                return new(VaultResult.Cancelled, "Proposal cancelled. Current rules are unchanged.");
            }
            catch (Exception) { return Unavailable(); }
        }
    }

    private void ValidateMandatoryBlacklist(VaultEdit edit)
    {
        if (_blacklist is null || !edit.Additions().Any()) return;
        var list = _blacklist.Current ?? throw new InvalidOperationException("Blacklist unavailable.");
        foreach (var addition in edit.Additions())
            if (SiteIdentity.TryCreate(addition.Host, out var site) && list.Contains(site))
                throw new ArgumentException("This hostname is on the permanent Blacklist and cannot be added to your Sphere.");
    }

    private VaultState ReadState()
    {
        var state = _store.ConfigurationState == AccessConfigurationState.NeedsSetup
            ? VaultState.CreateDevelopment(_clock.GetUtcNow()) : _store.LoadVault();
        state.Validate();
        return state;
    }

    private VaultOutcome? Authenticate(VaultState state, string password)
    {
        if (_store.ConfigurationState != AccessConfigurationState.Ready) return Unavailable();
        var now = Observe(state);
        if (_clockInvalid) return Unavailable();
        if (now < state.RetryAfter) return new(VaultResult.RetryLater, "Wait five seconds before trying your password again.");
        if (_store.Verify(password)) return null;
        now = Observe(state);
        if (_clockInvalid) return Unavailable();
        _store.SaveVault(state with { LastObservedUtc = now, RetryAfter = now.AddSeconds(5) });
        return new(VaultResult.WrongPassword, "The current password did not match. Try again in five seconds.");
    }

    private DateTimeOffset Observe(VaultState state)
    {
        var now = _anchorUtc + _clock.GetElapsedTime(_anchorTimestamp);
        var wall = _clock.GetUtcNow();
        if ((wall - now).Duration() > TimeSpan.FromSeconds(5) || wall.AddSeconds(5) < state.LastObservedUtc)
            _clockInvalid = true;
        return now;
    }

    private static VaultOutcome Unavailable() => new(VaultResult.Unavailable,
        "The Vault could not complete this safely. No change was applied. Check protected storage and your system clock.");
}
