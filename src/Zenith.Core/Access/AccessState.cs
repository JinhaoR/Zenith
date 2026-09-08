namespace Zenith.Core.Access;

public enum AccessConfigurationState { NeedsSetup, Ready, Unavailable }
public enum AccessPhase { SetupRequired, FirstChallenge, Cooldown, SecondChallenge, Granted, NotEligible, Unavailable, ClockInvalid }
public enum AccessSubmissionResult { Accepted, WrongPassword, RetryLater, NotReady, Unavailable }

public sealed record PendingAccessRequest(string Target, DateTimeOffset RequestedAt, DateTimeOffset EligibleAt,
    int CooldownSeconds = 1800, int GrantSeconds = 3600);
public sealed record AccessStateSnapshot(DateTimeOffset LastObservedUtc, DateTimeOffset RetryAfter,
    PendingAccessRequest[] Requests);
public sealed record AccessStatus(AccessPhase Phase, DateTimeOffset? EligibleAt = null,
    DateTimeOffset? ExpiresAt = null, DateTimeOffset? RetryAfter = null);
public sealed record AccessSubmission(AccessSubmissionResult Result, AccessStatus Status);

public interface IAccessStateStore
{
    object SyncRoot => this;
    AccessStateSnapshot Load();
    void Save(AccessStateSnapshot state);
}

public interface IAccessAuthenticator
{
    AccessConfigurationState ConfigurationState { get; }
    void Initialize(string password, DateTimeOffset now);
    bool Verify(string password);
}
