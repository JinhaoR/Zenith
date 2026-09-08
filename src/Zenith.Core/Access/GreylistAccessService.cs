using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.Core.Access;

public sealed class GreylistAccessService : IAccessGrantSource
{
    public static TimeSpan Cooldown { get; } = TimeSpan.FromMinutes(30);
    public static TimeSpan GrantLifetime { get; } = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(5);
    private readonly object _gate;
    private readonly ISitePolicySource _policy;
    private readonly IAccessStateStore _store;
    private readonly IAccessAuthenticator _authenticator;
    private readonly TimeProvider _clock;
    private readonly DateTimeOffset _anchorUtc;
    private readonly long _anchorTimestamp;
    private readonly Dictionary<SiteIdentity, AccessGrant> _grants = [];
    private bool _clockInvalid;
    private readonly IAccessRulesSource? _rules;
    private long _observedRevision = -1;

    public GreylistAccessService(ISitePolicySource policy, IAccessStateStore store,
        IAccessAuthenticator authenticator, TimeProvider? clock = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gate = store.SyncRoot;
        _rules = policy as IAccessRulesSource;
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _clock = clock ?? TimeProvider.System;
        _anchorUtc = _clock.GetUtcNow();
        _anchorTimestamp = _clock.GetTimestamp();
    }

    public AccessConfigurationState ConfigurationState => _authenticator.ConfigurationState;

    public AccessTiming? Timing
    {
        get
        {
            lock (_gate)
            {
                try { return ReadTiming(); }
                catch (Exception) { return null; }
            }
        }
    }

    private AccessTiming ReadTiming()
    {
        var timing = _rules?.GetAccessTiming() ?? new AccessTiming((int)Cooldown.TotalSeconds, (int)GrantLifetime.TotalSeconds, 0);
        new VaultSettings(timing.CooldownSeconds, timing.GrantSeconds).Validate();
        if (_observedRevision != timing.Revision)
        {
            _grants.Clear();
            _observedRevision = timing.Revision;
        }
        return timing;
    }

    public AccessStatus GetAvailability()
    {
        lock (_gate)
        {
            return TryReadState(out _, out _, out var failure) ? new(AccessPhase.FirstChallenge) : failure;
        }
    }

    public bool ConfigurePassword(string password)
    {
        lock (_gate)
        {
            if (password.Length is < 15 or > 128 || ConfigurationState != AccessConfigurationState.NeedsSetup)
            {
                return false;
            }
            try
            {
                _authenticator.Initialize(password, _clock.GetUtcNow());
                return ConfigurationState == AccessConfigurationState.Ready;
            }
            catch (Exception) { return false; }
        }
    }

    public AccessStatus GetStatus(string target)
    {
        lock (_gate)
        {
            if (!TryGetGreylistedTarget(target, out var normalized))
            {
                return new(AccessPhase.NotEligible);
            }
            if (!TryReadState(out var state, out var now, out var failure))
            {
                return failure;
            }
            return Describe(normalized!, state!, now);
        }
    }

    public IReadOnlyList<PendingAccessRequest> GetPendingRequests()
    {
        lock (_gate)
        {
            return TryReadState(out var state, out _, out _) ? state!.Requests.ToArray() : [];
        }
    }

    public IReadOnlyList<AccessGrant> GetActiveGrants()
    {
        lock (_gate)
        {
            if (!TryReadState(out _, out var now, out _)) return [];
            try
            {
                if (!_policy.TryGetActivePolicy(out var policy) || policy is null) return [];
                return _grants.Values
                    .Where(grant => grant.Covers(grant.Site, now) && policy.Classify(grant.Site) == AccessClass.Greylist)
                    .OrderBy(grant => grant.Site.Host, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception) { return []; }
        }
    }

    public AccessSubmission SubmitPassword(string target, string password)
    {
        lock (_gate)
        {
            if (!TryGetGreylistedTarget(target, out var normalized))
            {
                return new(AccessSubmissionResult.NotReady, new(AccessPhase.NotEligible));
            }
            if (!TryReadState(out var state, out var now, out var failure))
            {
                return new(AccessSubmissionResult.Unavailable, failure);
            }

            var before = Describe(normalized!, state!, now);
            if (before.Phase is not (AccessPhase.FirstChallenge or AccessPhase.SecondChallenge))
            {
                return new(AccessSubmissionResult.NotReady, before);
            }
            if (state!.RetryAfter > now)
            {
                return new(AccessSubmissionResult.RetryLater, before);
            }

            try
            {
                var verified = _authenticator.Verify(password);
                // Authentication may take time; recheck policy, clock and persisted state
                // before committing a transition or issuing authorization.
                if (!TryGetGreylistedTarget(target, out normalized) ||
                    !TryReadState(out state, out now, out failure))
                {
                    return new(AccessSubmissionResult.Unavailable, new(AccessPhase.Unavailable));
                }
                if (!verified)
                {
                    state = state! with { LastObservedUtc = now, RetryAfter = now.AddSeconds(5) };
                    _store.Save(state);
                    return new(AccessSubmissionResult.WrongPassword, Describe(normalized!, state, now));
                }

                var requests = state!.Requests.ToList();
                var timing = ReadTiming();
                var existing = requests.FindIndex(request => HostOf(request.Target) == normalized!.Site);
                var grantSeconds = existing < 0 ? timing.GrantSeconds : requests[existing].GrantSeconds;
                if (existing < 0)
                {
                    if (requests.Count >= 100)
                    {
                        return new(AccessSubmissionResult.Unavailable, new(AccessPhase.Unavailable));
                    }
                    requests.Add(new(target, now, now.AddSeconds(timing.CooldownSeconds), timing.CooldownSeconds, timing.GrantSeconds));
                }
                else if (requests[existing].EligibleAt <= now)
                {
                    requests.RemoveAt(existing);
                }
                else
                {
                    return new(AccessSubmissionResult.NotReady, Describe(normalized!, state, now));
                }

                state = new(now, DateTimeOffset.MinValue, requests.ToArray());
                _store.Save(state);
                if (existing >= 0)
                {
                    _grants[normalized!.Site] = new(normalized.Site, now, now.AddSeconds(grantSeconds));
                }
                return new(AccessSubmissionResult.Accepted, Describe(normalized!, state, now));
            }
            catch (Exception)
            {
                _grants.Clear();
                return new(AccessSubmissionResult.Unavailable, new(AccessPhase.Unavailable));
            }
        }
    }

    public bool TryGetGrant(SiteIdentity site, out AccessGrant? grant)
    {
        lock (_gate)
        {
            grant = null;
            if (!TryReadState(out _, out var now, out var failure))
            {
                if (failure.Phase == AccessPhase.SetupRequired)
                {
                    return false;
                }
                throw new InvalidOperationException("Temporary access state is unavailable.");
            }
            if (_grants.TryGetValue(site, out var current) && current.Covers(site, now))
            {
                grant = current;
                return true;
            }
            _grants.Remove(site);
            return false;
        }
    }

    private bool TryReadState(out AccessStateSnapshot? state, out DateTimeOffset now, out AccessStatus failure)
    {
        state = null;
        now = default;
        failure = new(AccessPhase.Unavailable);
        try
        {
            _ = ReadTiming();
            if (ConfigurationState == AccessConfigurationState.NeedsSetup)
            {
                failure = new(AccessPhase.SetupRequired);
                return false;
            }
            if (ConfigurationState != AccessConfigurationState.Ready)
            {
                _grants.Clear();
                return false;
            }
            state = _store.Load();
            Validate(state);
            now = _anchorUtc + _clock.GetElapsedTime(_anchorTimestamp);
            var wall = _clock.GetUtcNow();
            if (_clockInvalid || (wall - now).Duration() > ClockTolerance ||
                wall + ClockTolerance < state.LastObservedUtc)
            {
                _clockInvalid = true;
                _grants.Clear();
                failure = new(AccessPhase.ClockInvalid);
                return false;
            }
            if (now - state.LastObservedUtc >= TimeSpan.FromSeconds(10))
            {
                state = state with { LastObservedUtc = now };
                _store.Save(state);
            }
            return true;
        }
        catch (Exception)
        {
            _grants.Clear();
            return false;
        }
    }

    private AccessStatus Describe(NormalizedNavigationTarget target, AccessStateSnapshot state, DateTimeOffset now)
    {
        if (_grants.TryGetValue(target.Site, out var grant))
        {
            if (grant.Covers(target.Site, now))
            {
                return new(AccessPhase.Granted, ExpiresAt: grant.ExpiresAt);
            }
            _grants.Remove(target.Site);
        }
        var pending = state.Requests.FirstOrDefault(request => HostOf(request.Target) == target.Site);
        return pending is null
            ? new(AccessPhase.FirstChallenge, RetryAfter: state.RetryAfter)
            : new(pending.EligibleAt > now ? AccessPhase.Cooldown : AccessPhase.SecondChallenge,
                pending.EligibleAt, RetryAfter: state.RetryAfter);
    }

    private bool TryGetGreylistedTarget(string target, out NormalizedNavigationTarget? normalized)
    {
        normalized = null;
        try
        {
            return NavigationUriNormalizer.TryNormalize(target, out normalized) &&
                _policy.TryGetActivePolicy(out var policy) && policy is not null &&
                policy.Classify(normalized.Site) == AccessClass.Greylist;
        }
        catch (Exception) { return false; }
    }

    private static SiteIdentity HostOf(string target) =>
        NavigationUriNormalizer.TryNormalize(target, out var normalized)
            ? normalized.Site : throw new InvalidOperationException("Invalid pending destination.");

    private static void Validate(AccessStateSnapshot state)
    {
        if (state.LastObservedUtc < DateTimeOffset.UnixEpoch || state.Requests is null || state.Requests.Length > 100 ||
            state.RetryAfter > state.LastObservedUtc.AddSeconds(5))
        {
            throw new InvalidOperationException("Invalid access snapshot.");
        }
        var sites = new HashSet<SiteIdentity>();
        foreach (var request in state.Requests)
        {
            if (request is null || request.RequestedAt < DateTimeOffset.UnixEpoch ||
                request.CooldownSeconds is < 5 or > 86400 || request.GrantSeconds is < 5 or > 86400 ||
                request.RequestedAt > state.LastObservedUtc || request.EligibleAt - request.RequestedAt != TimeSpan.FromSeconds(request.CooldownSeconds) ||
                !sites.Add(HostOf(request.Target)))
            {
                throw new InvalidOperationException("Invalid pending request.");
            }
        }
    }
}
