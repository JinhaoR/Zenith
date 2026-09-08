using Zenith.Core.Access;
using Zenith.Core.Navigation;

namespace Zenith.Core.Tests.Access;

public sealed class GreylistAccessServiceTests
{
    private const string Target = "https://outside.example/article";
    private const string Password = "test password for access";

    [Fact]
    public void FirstChallengeStartsWaitAndCannotGrantEarly()
    {
        var fixture = new Fixture();
        Assert.Equal(AccessPhase.FirstChallenge, fixture.Service.GetStatus(Target).Phase);
        var first = fixture.Service.SubmitPassword(Target, Password);
        Assert.Equal(AccessSubmissionResult.Accepted, first.Result);
        Assert.Equal(AccessPhase.Cooldown, first.Status.Phase);
        Assert.Equal(fixture.Clock.Now.AddMinutes(30), first.Status.EligibleAt);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1));
        Assert.Equal(AccessSubmissionResult.NotReady, fixture.Service.SubmitPassword(Target, Password).Result);
        Assert.Equal(1, fixture.State.Verifications);
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
    }

    [Fact]
    public void SecondChallengeIsRequiredAndGrantExpiresAtSixtyMinutes()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(AccessPhase.SecondChallenge, fixture.Service.GetStatus(Target).Phase);
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
        var second = fixture.Service.SubmitPassword(Target, Password);
        Assert.Equal(AccessPhase.Granted, second.Status.Phase);
        Assert.Equal(fixture.Clock.Now.AddHours(1), second.Status.ExpiresAt);
        Assert.Empty(fixture.State.Snapshot.Requests);
        Assert.True(fixture.Service.TryGetGrant(Host(), out _));
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
        Assert.Equal(AccessPhase.FirstChallenge, fixture.Service.GetStatus(Target).Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncorrectPasswordCannotAdvanceEitherChallengeAndRetryDelaySurvivesRestart(bool secondChallenge)
    {
        var fixture = new Fixture();
        if (secondChallenge)
        {
            fixture.Service.SubmitPassword(Target, Password);
            fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        }
        var denied = fixture.Service.SubmitPassword(Target, "wrong password");
        Assert.Equal(AccessSubmissionResult.WrongPassword, denied.Result);
        var restarted = fixture.Restart();
        Assert.Equal(AccessSubmissionResult.RetryLater, restarted.SubmitPassword(Target, Password).Result);
        Assert.False(restarted.TryGetGrant(Host(), out _));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(AccessSubmissionResult.Accepted, restarted.SubmitPassword(Target, Password).Result);
    }

    [Fact]
    public void CooldownsSurviveRestartButGrantsAndConsumedChallengesDoNot()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        var restarted = fixture.Restart();
        Assert.Equal(AccessPhase.Cooldown, restarted.GetStatus(Target).Phase);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(AccessPhase.SecondChallenge, restarted.GetStatus(Target).Phase);
        Assert.Equal(AccessPhase.Granted, restarted.SubmitPassword(Target, Password).Status.Phase);
        var afterGrantRestart = fixture.Restart();
        Assert.False(afterGrantRestart.TryGetGrant(Host(), out _));
        Assert.Equal(AccessPhase.FirstChallenge, afterGrantRestart.GetStatus(Target).Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPersistenceCannotAdvanceAccess(bool secondChallenge)
    {
        var fixture = new Fixture();
        if (secondChallenge)
        {
            fixture.Service.SubmitPassword(Target, Password);
            fixture.Clock.Advance(TimeSpan.FromMinutes(30));
            _ = fixture.Service.GetStatus(Target); // checkpoint before failing the transition write
        }
        fixture.State.FailWrites = true;
        Assert.Equal(AccessSubmissionResult.Unavailable, fixture.Service.SubmitPassword(Target, Password).Result);
        fixture.State.FailWrites = false;
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
        Assert.Equal(secondChallenge ? AccessPhase.SecondChallenge : AccessPhase.FirstChallenge,
            fixture.Restart().GetStatus(Target).Phase);
    }

    [Fact]
    public void BlacklistingDuringCooldownPreventsSecondChallenge()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Policy.Snapshot = new([new SitePolicyEntry("outside.example", AccessClass.Blacklist)]);
        Assert.Equal(AccessPhase.NotEligible, fixture.Service.GetStatus(Target).Phase);
        Assert.Equal(AccessSubmissionResult.NotReady, fixture.Service.SubmitPassword(Target, Password).Result);
        Assert.Equal(1, fixture.State.Verifications);
    }

    [Theory]
    [InlineData(3600)]
    [InlineData(-60)]
    public void ClockJumpsDisableAccessAndInvalidateExistingGrants(int jumpSeconds)
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(jumpSeconds);
        Assert.Equal(AccessPhase.ClockInvalid, fixture.Service.GetStatus(Target).Phase);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.TryGetGrant(Host(), out _));
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(-jumpSeconds);
        Assert.Equal(AccessPhase.ClockInvalid, fixture.Service.GetStatus(Target).Phase);
    }

    [Fact]
    public void RestartRejectsRollbackBelowPersistedHighWaterMark()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));
        _ = fixture.Service.GetStatus(Target);
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(-5);
        Assert.Equal(AccessPhase.ClockInvalid, fixture.Restart().GetStatus(Target).Phase);
    }

    [Fact]
    public void InvalidPersistedDeadlineFailsClosed()
    {
        var fixture = new Fixture();
        fixture.State.Snapshot = fixture.State.Snapshot with
        {
            Requests = [new(Target, fixture.Clock.Now, fixture.Clock.Now.AddSeconds(1))]
        };
        Assert.Equal(AccessPhase.Unavailable, fixture.Service.GetStatus(Target).Phase);
        Assert.Equal(AccessSubmissionResult.Unavailable, fixture.Service.SubmitPassword(Target, Password).Result);
        Assert.Equal(0, fixture.State.Verifications);
    }

    [Fact]
    public async Task ConcurrentSubmissionsCannotSkipCooldown()
    {
        var fixture = new Fixture();
        var submissions = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => fixture.Service.SubmitPassword(Target, Password))));
        Assert.Single(submissions, submission => submission.Result == AccessSubmissionResult.Accepted);
        Assert.Equal(1, fixture.State.Verifications);
        Assert.Equal(AccessPhase.Cooldown, fixture.Service.GetStatus(Target).Phase);
    }

    [Fact]
    public void PathsOnTheSameHostnameShareOneWait()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        var secondPath = fixture.Service.SubmitPassword("https://OUTSIDE.EXAMPLE:443/other", Password);
        Assert.Equal(AccessSubmissionResult.NotReady, secondPath.Result);
        Assert.Single(fixture.State.Snapshot.Requests);
    }

    [Fact]
    public void UnreadableStateRevokesAGrantWithoutRevivingItWhenStorageReturns()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Service.SubmitPassword(Target, Password);
        fixture.State.FailReads = true;
        Assert.Equal(AccessPhase.Unavailable, fixture.Service.GetStatus(Target).Phase);
        fixture.State.FailReads = false;
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
        Assert.Equal(AccessPhase.FirstChallenge, fixture.Service.GetStatus(Target).Phase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolicyChangeDuringAuthenticationCannotAdvanceEitherChallenge(bool secondChallenge)
    {
        var fixture = new Fixture();
        if (secondChallenge)
        {
            fixture.Service.SubmitPassword(Target, Password);
            fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        }
        fixture.State.OnVerify = () => fixture.Policy.Snapshot = new(
            [new SitePolicyEntry("outside.example", AccessClass.Blacklist)]);
        Assert.Equal(AccessSubmissionResult.Unavailable, fixture.Service.SubmitPassword(Target, Password).Result);
        Assert.False(fixture.Service.TryGetGrant(Host(), out _));
        Assert.Equal(secondChallenge ? 1 : 0, fixture.State.Snapshot.Requests.Length);
    }

    [Fact]
    public void MissingOrUnreadableInitializedStateDoesNotOfferSetup()
    {
        var fixture = new Fixture();
        fixture.State.ConfigurationState = AccessConfigurationState.Unavailable;
        Assert.Equal(AccessPhase.Unavailable, fixture.Service.GetStatus(Target).Phase);
        Assert.False(fixture.Service.ConfigurePassword(Password));
    }

    private static SiteIdentity Host()
    {
        Assert.True(SiteIdentity.TryCreate("outside.example", out var site));
        return site;
    }

    [Fact]
    public void ActiveVisitsOnlyIncludeCurrentAuthorizedSessionGrants()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        Assert.Empty(fixture.Service.GetActiveGrants());
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Service.SubmitPassword(Target, Password);
        Assert.Equal(Host(), Assert.Single(fixture.Service.GetActiveGrants()).Site);
        Assert.Empty(fixture.Restart().GetActiveGrants());
        fixture.Policy.Snapshot = new([new SitePolicyEntry("outside.example", AccessClass.Blacklist)]);
        Assert.Empty(fixture.Service.GetActiveGrants());
        fixture.Policy.Snapshot = new([]);
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(fixture.Service.GetActiveGrants());
    }

    [Fact]
    public void UnreadableStateHidesActiveVisits()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Service.SubmitPassword(Target, Password);
        fixture.State.FailReads = true;
        Assert.Empty(fixture.Service.GetActiveGrants());
    }

    [Fact]
    public void ClockFailureHidesActiveVisits()
    {
        var fixture = new Fixture();
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        fixture.Service.SubmitPassword(Target, Password);
        fixture.Clock.Now -= TimeSpan.FromMinutes(1);
        Assert.Empty(fixture.Service.GetActiveGrants());
    }

    private sealed class Fixture
    {
        public ManualClock Clock { get; } = new();
        public MemoryState State { get; }
        public MutablePolicy Policy { get; } = new();
        public GreylistAccessService Service { get; }
        public Fixture()
        {
            State = new(Clock.Now);
            Service = Restart();
        }
        public GreylistAccessService Restart() => new(Policy, State, State, Clock);
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan duration) { Now += duration; _timestamp += duration.Ticks; }
    }

    private sealed class MemoryState(DateTimeOffset now) : IAccessStateStore, IAccessAuthenticator
    {
        public AccessStateSnapshot Snapshot { get; set; } = new(now, DateTimeOffset.MinValue, []);
        public AccessConfigurationState ConfigurationState { get; set; } = AccessConfigurationState.Ready;
        public bool FailWrites { get; set; }
        public bool FailReads { get; set; }
        public Action? OnVerify { get; set; }
        public int Verifications { get; private set; }
        public AccessStateSnapshot Load() => FailReads ? throw new InvalidOperationException("Test read failure") : Snapshot;
        public void Save(AccessStateSnapshot state)
        {
            if (FailWrites) { throw new InvalidOperationException("Test write failure"); }
            Snapshot = state;
        }
        public void Initialize(string password, DateTimeOffset now) => ConfigurationState = AccessConfigurationState.Ready;
        public bool Verify(string password) { Verifications++; OnVerify?.Invoke(); return password == Password; }
    }

    private sealed class MutablePolicy : ISitePolicySource
    {
        public SitePolicySnapshot Snapshot { get; set; } = new([]);
        public bool TryGetActivePolicy(out SitePolicySnapshot snapshot) { snapshot = Snapshot; return true; }
    }
}
