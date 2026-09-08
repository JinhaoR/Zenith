using Zenith.Core.Access;
using Zenith.Core.Navigation;
using Zenith.Core.Vault;

namespace Zenith.Core.Tests.Access;

public sealed class VaultServiceTests
{
    private const string Password = "the existing password";
    private const string Replacement = "the replacement password";

    [Fact]
    public void MandatoryBlacklistCannotBeOverriddenAndIsRecheckedAtConfirmation()
    {
        var f = new Fixture();
        var source = new BlacklistSource();
        var service = new VaultService(f.Store, f.Clock, source);
        Assert.False(service.TryGetActivePolicy(out _));
        source.Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example");
        Assert.Throws<ArgumentException>(() => service.Review(new(AddHost: "blocked.example")));
        Assert.Equal(VaultResult.Invalid, service.Stage(new(AddHost: "blocked.example"), Password).Result);
        service.Stage(new(AddHost: "new.example"), Password);
        var pending = f.Store.Vault.Pending!;
        source.Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example new.example github.com");
        f.Clock.Advance(5);
        Assert.NotEqual(VaultResult.Applied, service.Confirm(pending.Id, Password).Result);
        Assert.Equal(0, f.Store.Vault.Revision);
        Assert.Equal(AccessClass.Blacklist, Classify(service, "github.com"));
        Assert.Equal(VaultResult.Staged, service.Stage(new(AddHost: "example", IncludeSubdomains: true), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);
        Assert.Equal(AccessClass.Blacklist, Classify(service, "blocked.example"));
    }

    private sealed class BlacklistSource : Zenith.Core.Filtering.IBlacklistSource
    {
        public Zenith.Core.Filtering.HostsBlacklist? Current { get; set; }
    }

    [Fact]
    public void ReviewingNormalizesWithoutAuthenticationOrPersistence()
    {
        var f = new Fixture();
        var before = f.Store.Vault;
        f.Store.FailWrites = true;
        var review = f.Service.Review(new(AddHost: "EXAMPLE.NET."));
        Assert.Equal("example.net", review.Edit.AddHost);
        Assert.Equal(before.Settings, review.ActiveSettings);
        Assert.Equal(before.Revision, review.Revision);
        Assert.Same(before, f.Store.Vault);
        Assert.Equal(0, f.Store.Verifications);
    }

    [Fact]
    public void ReviewRejectsInvalidNoOpAndDuplicateEdits()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Sites = [new("example.net", AccessClass.Whitelist, false)] };
        Assert.Throws<ArgumentException>(() => f.Service.Review(new()));
        Assert.Throws<ArgumentException>(() => f.Service.Review(new(AddHost: "https://invalid.example/path")));
        Assert.Throws<ArgumentException>(() => f.Service.Review(new(AddHost: "EXAMPLE.NET.")));
        Assert.Throws<ArgumentException>(() => f.Service.Review(new(GreylistSeconds: 4)));
        Assert.Null(f.Store.Vault.Pending);
        Assert.Equal(0, f.Store.Verifications);
    }

    [Fact]
    public void ExactEntryCanBeExpandedToSubdomainsThroughTheProtectedFlow()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with
        {
            Sites = [new("example.net", AccessClass.Whitelist, false)]
        };

        var review = f.Service.Review(new(AddHost: "example.net", IncludeSubdomains: true));
        Assert.True(review.Edit.IncludeSubdomains);
        Assert.Equal(VaultResult.Staged, f.Service.Stage(review.Edit, Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied,
            f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);

        var entry = Assert.Single(f.Store.Vault.Sites);
        Assert.Equal("example.net", entry.Host);
        Assert.True(entry.IncludeSubdomains);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "future.example.net"));
    }

    [Fact]
    public void ParentScopeMakesChildAdditionsRedundant()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with
        {
            Sites = [new("example.net", AccessClass.Whitelist, true)]
        };

        var error = Assert.Throws<ArgumentException>(() =>
            f.Service.Review(new(AddHost: "docs.example.net", IncludeSubdomains: true)));
        Assert.Contains("broader Sphere entry", error.Message);
        Assert.Equal(VaultResult.Invalid,
            f.Service.Stage(new(AddHost: "docs.example.net", IncludeSubdomains: true), Password).Result);
        Assert.Null(f.Store.Vault.Pending);
    }

    [Fact]
    public void AddingBroadParentScopeRemovesOnlyRedundantWhitelistChildren()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with
        {
            Sites =
            [
                new("docs.example.net", AccessClass.Whitelist, false),
                new("deep.example.net", AccessClass.Whitelist, true),
                new("blocked.example.net", AccessClass.Blacklist, false)
            ]
        };

        Assert.Equal(VaultResult.Staged,
            f.Service.Stage(new(AddHost: "example.net", IncludeSubdomains: true), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied,
            f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);

        Assert.Contains(f.Store.Vault.Sites, site =>
            site.Host == "example.net" && site.AccessClass == AccessClass.Whitelist && site.IncludeSubdomains);
        Assert.DoesNotContain(f.Store.Vault.Sites, site => site.Host == "docs.example.net");
        Assert.DoesNotContain(f.Store.Vault.Sites, site => site.Host == "deep.example.net");
        Assert.Contains(f.Store.Vault.Sites, site =>
            site.Host == "blocked.example.net" && site.AccessClass == AccessClass.Blacklist);
        Assert.Equal(AccessClass.Blacklist, Classify(f.Service, "blocked.example.net"));
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "other.example.net"));
    }

    [Fact]
    public void RemovingParentScopeIsStagedAndRemovesCoveredWhitelistChildrenOnly()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with
        {
            Sites =
            [
                new("example.net", AccessClass.Whitelist, true),
                new("docs.example.net", AccessClass.Whitelist, false),
                new("blocked.example.net", AccessClass.Blacklist, false),
                new("unrelated.net", AccessClass.Whitelist, false)
            ]
        };

        var review = f.Service.Review(new(RemoveHost: "EXAMPLE.NET."));
        Assert.Equal("example.net", review.Edit.RemoveHost);
        Assert.Equal(VaultResult.Staged, f.Service.Stage(review.Edit, Password).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "example.net"));
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "docs.example.net"));

        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied,
            f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);

        Assert.DoesNotContain(f.Store.Vault.Sites, site => site.Host == "example.net");
        Assert.DoesNotContain(f.Store.Vault.Sites, site => site.Host == "docs.example.net");
        Assert.Contains(f.Store.Vault.Sites, site => site.Host == "blocked.example.net" &&
            site.AccessClass == AccessClass.Blacklist);
        Assert.Contains(f.Store.Vault.Sites, site => site.Host == "unrelated.net");
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "example.net"));
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "docs.example.net"));
        Assert.Equal(AccessClass.Blacklist, Classify(f.Service, "blocked.example.net"));
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "unrelated.net"));
    }

    [Fact]
    public void RemovalMustNameAnIndependentStoredWhitelistScope()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with
        {
            Sites =
            [
                new("example.net", AccessClass.Whitelist, true),
                new("docs.example.net", AccessClass.Whitelist, false)
            ]
        };

        Assert.Throws<ArgumentException>(() => f.Service.Review(new(RemoveHost: "missing.net")));
        Assert.Throws<ArgumentException>(() => f.Service.Review(new(RemoveHost: "docs.example.net")));
        Assert.Throws<ArgumentException>(() => f.Service.Review(new(
            AddHost: "new.example", RemoveHost: "example.net")));
        Assert.Equal(VaultResult.Invalid,
            f.Service.Stage(new(RemoveHost: "docs.example.net"), Password).Result);
        Assert.Null(f.Store.Vault.Pending);
    }

    [Fact]
    public void IndependentWhitelistScopesCollapseCoveredChildren()
    {
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var state = new VaultState(0, new(),
        [
            new("example.net", AccessClass.Whitelist, true),
            new("docs.example.net", AccessClass.Whitelist, false),
            new("other.net", AccessClass.Whitelist, false),
            new("blocked.example.net", AccessClass.Blacklist, false)
        ], now, DateTimeOffset.MinValue);

        Assert.Equal(["example.net", "other.net"],
            state.GetIndependentWhitelistScopes().Select(site => site.Host));
    }

    [Fact]
    public void StaleReviewCannotReplacePendingProposalOrAuthenticate()
    {
        var f = new Fixture();
        var review = f.Service.Review(new(AddHost: "example.net"));
        f.Service.Stage(new(VaultSeconds: 60), Password);
        f.Clock.Advance(5);
        f.Service.Confirm(f.Store.Vault.Pending!.Id, Password);
        f.Service.Stage(new(AddHost: "another.net"), Password);
        var before = f.Store.Vault;
        var verifications = f.Store.Verifications;
        Assert.Equal(VaultResult.Stale,
            f.Service.Stage(review.Edit, Password, expectedRevision: review.Revision).Result);
        Assert.Same(before, f.Store.Vault);
        Assert.Equal(verifications, f.Store.Verifications);
    }

    [Fact]
    public void TestDefaultsAreFiveSecondsAndNeverSkipAuthentication()
    {
        var f = new Fixture();
        Assert.Equal(new VaultSettings(5, 5, 5), f.Store.Vault.Settings);
        Assert.Equal(VaultResult.WrongPassword, f.Service.Stage(new(AddHost: "example.net"), "wrong").Result);
        Assert.Null(f.Store.Vault.Pending);
        Assert.Equal(VaultResult.RetryLater, f.Service.Stage(new(AddHost: "example.net"), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Staged, f.Service.Stage(new(AddHost: "example.net"), Password).Result);
        var id = f.Store.Vault.Pending!.Id;
        Assert.Equal(VaultResult.TooEarly, f.Service.Confirm(id, Password).Result);
        f.Clock.Advance(4);
        Assert.Equal(VaultResult.TooEarly, f.Service.Confirm(id, Password).Result);
        f.Clock.Advance(1);
        Assert.Equal(VaultResult.WrongPassword, f.Service.Confirm(id, "wrong").Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(id, Password).Result);
        Assert.Equal(1, f.Store.Vault.Revision);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "example.net"));
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "sub.example.net"));
        Assert.Equal(VaultResult.Stale, f.Service.Confirm(id, Password).Result);
    }

    [Fact]
    public void RestartPreservesWaitAndConfirmationIsNeverAutomatic()
    {
        var f = new Fixture();
        f.Service.Stage(new(AddHost: "example.net"), Password);
        var id = f.Store.Vault.Pending!.Id;
        f.Clock.Advance(3);
        var restarted = new VaultService(f.Store, f.Clock);
        Assert.Equal(VaultPhase.Waiting, restarted.GetStatus().Phase);
        f.Clock.Advance(2);
        Assert.Equal(VaultPhase.Eligible, restarted.GetStatus().Phase);
        Assert.Equal(AccessClass.Greylist, Classify(restarted, "example.net"));
        Assert.Equal(VaultResult.Applied, restarted.Confirm(id, Password).Result);
    }

    [Fact]
    public void ReducingVaultWaitMustUseTheOldDelayAndReplacementStartsItAgain()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Settings = new(5, 5, 100) };
        f.Service.Stage(new(VaultSeconds: 5), Password);
        var old = f.Store.Vault.Pending!;
        f.Clock.Advance(99);
        Assert.Equal(VaultResult.TooEarly, f.Service.Confirm(old.Id, Password).Result);
        f.Service.Stage(new(VaultSeconds: 5, AddHost: "example.net"), Password);
        Assert.Equal(f.Clock.Now.AddSeconds(100), f.Store.Vault.Pending!.EligibleAt);
        Assert.Equal(VaultResult.Stale, f.Service.Confirm(old.Id, Password).Result);
        Assert.Equal(100, f.Store.Vault.Settings.VaultSeconds);
        f.Clock.Advance(100);
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(f.Store.Vault.Pending.Id, Password).Result);
        Assert.Equal(5, f.Store.Vault.Settings.VaultSeconds);
    }

    [Fact]
    public void PasswordRotationKeepsOldPasswordUntilAtomicConfirmation()
    {
        var f = new Fixture();
        f.Service.Stage(new(ChangePassword: true), Password, Replacement);
        Assert.True(f.Store.Verify(Password));
        Assert.False(f.Store.Verify(Replacement));
        f.Clock.Advance(5);
        var id = f.Store.Vault.Pending!.Id;
        f.Store.FailWrites = true;
        Assert.Equal(VaultResult.Unavailable, f.Service.Confirm(id, Password).Result);
        Assert.True(f.Store.Verify(Password));
        Assert.NotNull(f.Store.Vault.Pending);
        f.Store.FailWrites = false;
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(id, Password).Result);
        Assert.False(f.Store.Verify(Password));
        Assert.True(f.Store.Verify(Replacement));
    }

    [Fact]
    public void CancelDiscardsProposalWithoutChangingPolicyOrPassword()
    {
        var f = new Fixture();
        f.Service.Stage(new(AddHost: "example.net", ChangePassword: true), Password, Replacement);
        Assert.Equal(VaultResult.Cancelled, f.Service.Cancel(f.Store.Vault.Pending!.Id).Result);
        Assert.Null(f.Store.Vault.Pending);
        Assert.Equal(0, f.Store.Vault.Revision);
        Assert.True(f.Store.Verify(Password));
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "example.net"));
    }

    [Theory]
    [InlineData("https://example.net/path")]
    [InlineData("user@example.net")]
    [InlineData("example.net:443")]
    [InlineData("*.example.net")]
    public void InvalidHostsCannotStageAProposal(string host)
    {
        var f = new Fixture();
        Assert.Equal(VaultResult.Invalid, f.Service.Stage(new(AddHost: host), Password).Result);
        Assert.Null(f.Store.Vault.Pending);
    }

    [Fact]
    public void ExplicitSubdomainsDoNotOverrideBlacklist()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Sites = [new("blocked.example.net", AccessClass.Blacklist, true)] };
        Assert.Equal(VaultResult.Invalid, f.Service.Stage(new(AddHost: "blocked.example.net"), Password).Result);
        Assert.Equal(VaultResult.Staged, f.Service.Stage(new(AddHost: "EXAMPLE.NET.", IncludeSubdomains: true), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "sub.example.net"));
        Assert.Equal(AccessClass.Blacklist, Classify(f.Service, "blocked.example.net"));
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "notexample.net"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(86401)]
    public void InvalidTimingCannotBePersisted(int value)
    {
        var f = new Fixture();
        Assert.Equal(VaultResult.Invalid, f.Service.Stage(new(GreylistSeconds: value), Password).Result);
        Assert.Null(f.Store.Vault.Pending);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(-60)]
    public void ClockAnomaliesCannotCompleteChanges(int seconds)
    {
        var f = new Fixture();
        f.Service.Stage(new(AddHost: "example.net"), Password);
        f.Clock.JumpWall(seconds);
        Assert.Equal(VaultPhase.ClockInvalid, f.Service.GetStatus().Phase);
        Assert.Equal(VaultResult.Unavailable, f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);
    }

    [Fact]
    public void CorruptStateFailsClosedWithoutStarterFallback()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Settings = new(0) };
        Assert.Equal(VaultPhase.Unavailable, f.Service.GetStatus().Phase);
        Assert.False(f.Service.TryGetActivePolicy(out _));
    }

    [Fact]
    public void ExistingGreylistDeadlinesAreCapturedAndNewRequestsUseConfirmedSettings()
    {
        var f = new Fixture();
        var access = new GreylistAccessService(f.Service, f.Store, f.Store, f.Clock);
        const string target = "https://outside.example/";
        access.SubmitPassword(target, Password);
        Assert.Equal(f.Clock.Now.AddSeconds(5), access.GetStatus(target).EligibleAt);
        f.Service.Stage(new(GreylistSeconds: 60, GrantSeconds: 120), Password);
        f.Clock.Advance(5);
        f.Service.Confirm(f.Store.Vault.Pending!.Id, Password);
        Assert.Equal(AccessPhase.Granted, access.SubmitPassword(target, Password).Status.Phase);
        Assert.Equal(f.Clock.Now.AddSeconds(5), access.GetStatus(target).ExpiresAt);
        var next = access.SubmitPassword("https://another.example/", Password);
        Assert.Equal(f.Clock.Now.AddSeconds(60), next.Status.EligibleAt);
    }

    [Fact]
    public async Task ConcurrentConfirmationAppliesExactlyOnce()
    {
        var f = new Fixture();
        f.Service.Stage(new(AddHost: "example.net"), Password);
        var id = f.Store.Vault.Pending!.Id;
        f.Clock.Advance(5);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => f.Service.Confirm(id, Password))));
        Assert.Single(outcomes, outcome => outcome.Result == VaultResult.Applied);
        Assert.Equal(1, f.Store.Vault.Revision);
    }

    [Fact]
    public void MultiDayWaitsUseTheSameGateAndSurviveRestart()
    {
        var f = new Fixture();
        f.Service.Stage(new(VaultSeconds: 259200), Password);
        f.Clock.Advance(5);
        f.Service.Confirm(f.Store.Vault.Pending!.Id, Password);
        f.Service.Stage(new(AddHost: "example.net"), Password);
        var id = f.Store.Vault.Pending!.Id;
        f.Clock.Advance(259199);
        var restarted = new VaultService(f.Store, f.Clock);
        Assert.Equal(VaultResult.TooEarly, restarted.Confirm(id, Password).Result);
        f.Clock.Advance(1);
        Assert.Equal(VaultResult.Applied, restarted.Confirm(id, Password).Result);
    }

    [Fact]
    public void FailedStagingLeavesExistingProposalIntact()
    {
        var f = new Fixture();
        f.Service.Stage(new(AddHost: "example.net"), Password);
        var previous = f.Store.Vault.Pending;
        f.Store.FailWrites = true;
        Assert.Equal(VaultResult.Unavailable, f.Service.Stage(new(AddHost: "another.net"), Password).Result);
        Assert.Equal(previous, f.Store.Vault.Pending);
    }

    [Fact]
    public void ConfirmedRevisionRevokesExistingSessionGrants()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Settings = new(5, 120, 5) };
        var access = new GreylistAccessService(f.Service, f.Store, f.Store, f.Clock);
        const string target = "https://outside.example/";
        access.SubmitPassword(target, Password);
        f.Clock.Advance(5);
        access.SubmitPassword(target, Password);
        Assert.Equal(AccessPhase.Granted, access.GetStatus(target).Phase);
        f.Service.Stage(new(ChangePassword: true), Password, Replacement);
        f.Clock.Advance(5);
        f.Service.Confirm(f.Store.Vault.Pending!.Id, Password);
        Assert.Equal(AccessPhase.FirstChallenge, access.GetStatus(target).Phase);
    }

    private static AccessClass Classify(VaultService service, string host)
    {
        Assert.True(service.TryGetActivePolicy(out var policy));
        Assert.True(SiteIdentity.TryCreate(host, out var site));
        return policy.Classify(site);
    }

    [Fact]
    public void BatchReplacesBroadGoogleWithSelectedServicesAtomically()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Sites =
        [
            new("google.com", AccessClass.Whitelist, true),
            new("scholar.google.com", AccessClass.Whitelist, true),
            new("blocked.google.com", AccessClass.Blacklist, false)
        ] };
        var edit = new VaultEdit(AddSites:
        [
            new("scholar.google.com", DisplayName: "Google Scholar"),
            new("drive.google.com", DisplayName: "Google Drive"),
            new("mail.google.com", DisplayName: "Gmail")
        ], RemoveSites: ["google.com"]);
        Assert.Equal(VaultResult.Staged, f.Service.Stage(edit, Password).Result);
        var id = f.Store.Vault.Pending!.Id;
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "www.google.com"));
        Assert.Equal(VaultResult.TooEarly, f.Service.Confirm(id, Password).Result);
        f.Clock.Advance(5);
        f.Store.FailWrites = true;
        Assert.Equal(VaultResult.Unavailable, f.Service.Confirm(id, Password).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "www.google.com"));
        f.Store.FailWrites = false;
        Assert.Equal(VaultResult.Applied, new VaultService(f.Store, f.Clock).Confirm(id, Password).Result);
        foreach (var host in new[] { "scholar.google.com", "drive.google.com", "mail.google.com" })
            Assert.Equal(AccessClass.Whitelist, Classify(f.Service, host));
        foreach (var host in new[] { "google.com", "www.google.com", "accounts.google.com", "maps.google.com", "sub.scholar.google.com" })
            Assert.Equal(AccessClass.Greylist, Classify(f.Service, host));
        Assert.Equal(AccessClass.Blacklist, Classify(f.Service, "blocked.google.com"));
        Assert.Equal("Gmail", f.Store.Vault.Sites.Single(site => site.Host == "mail.google.com").DisplayName);
        Assert.Equal(1, f.Store.Vault.Revision);
    }

    [Fact]
    public void ServiceSubdomainsNeverAuthorizeParentsOrSiblings()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Sites = [] };
        Assert.Equal(VaultResult.Staged, f.Service.Stage(new(AddSites:
            [new("scholar.google.com", true)], RemoveSites: []), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "sub.scholar.google.com"));
        foreach (var host in new[] { "google.com", "www.google.com", "mail.google.com", "notscholar.google.com", "scholar.google.com.evil.test" })
            Assert.Equal(AccessClass.Greylist, Classify(f.Service, host));
    }

    [Fact]
    public void BatchCanNarrowExistingScopeAndRemoveSeveralSites()
    {
        var f = new Fixture();
        f.Store.Vault = f.Store.Vault with { Sites =
            [new("service.example", AccessClass.Whitelist, true), new("one.example", AccessClass.Whitelist, false), new("two.example", AccessClass.Whitelist, false)] };
        Assert.Equal(VaultResult.Staged, f.Service.Stage(new(AddSites: [new("service.example")],
            RemoveSites: ["one.example", "two.example"]), Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Service.Confirm(f.Store.Vault.Pending!.Id, Password).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "service.example"));
        foreach (var host in new[] { "child.service.example", "one.example", "two.example" })
            Assert.Equal(AccessClass.Greylist, Classify(f.Service, host));
    }

    [Fact]
    public void BatchRejectsMalformedConflictingAndBlockedSelectionsWithoutPartialChanges()
    {
        var f = new Fixture();
        var original = f.Store.Vault;
        var invalid = new VaultEdit[]
        {
            new(AddSites: [new("new.example")]),
            new(AddSites: [new("new.example"), new("NEW.EXAMPLE.")], RemoveSites: []),
            new(AddSites: [new("127.0.0.1", true)], RemoveSites: []),
            new(AddSites: [new("https://example.com")], RemoveSites: []),
            new(AddSites: [null!], RemoveSites: []),
            new(AddSites: [], RemoveSites: ["github.com", "GITHUB.COM."]),
            new(AddSites: [], RemoveSites: ["unknown.example"]),
            new(AddHost: "new.example", AddSites: [], RemoveSites: [])
        };
        foreach (var edit in invalid)
        {
            Assert.Equal(VaultResult.Invalid, f.Service.Stage(edit, Password).Result);
            Assert.Same(original, f.Store.Vault);
        }
        var source = new BlacklistSource { Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 blocked.example") };
        var service = new VaultService(f.Store, f.Clock, source);
        Assert.Equal(VaultResult.Invalid, service.Stage(new(AddSites: [new("good.example"), new("blocked.example")],
            RemoveSites: ["github.com"]), Password).Result);
        Assert.Same(original, f.Store.Vault);
        Assert.Equal(VaultResult.Staged, service.Stage(new(AddSites: [new("new.example")], RemoveSites: ["github.com"]), Password).Result);
        var pending = f.Store.Vault.Pending!;
        source.Current = Zenith.Core.Filtering.HostsBlacklist.Parse("0.0.0.0 new.example");
        f.Clock.Advance(5);
        Assert.NotEqual(VaultResult.Applied, service.Confirm(pending.Id, Password).Result);
        Assert.Equal(0, f.Store.Vault.Revision);
        Assert.Contains(f.Store.Vault.Sites, site => site.Host == "github.com");
    }

    [Fact]
    public void ReviewedBatchOwnsItsSelectionsAndCancellationDoesNotApplyThem()
    {
        var f = new Fixture();
        var additions = new List<VaultSiteAddition> { new("new.example") };
        var removals = new List<string> { "github.com" };
        var review = f.Service.Review(new(AddSites: additions, RemoveSites: removals));
        additions.Clear(); removals.Clear();
        Assert.Single(review.Edit.Additions());
        Assert.Single(review.Edit.Removals());
        Assert.Equal(VaultResult.Staged, f.Service.Stage(review.Edit, Password).Result);
        Assert.Equal(VaultResult.Cancelled, f.Service.Cancel(f.Store.Vault.Pending!.Id).Result);
        Assert.Equal(AccessClass.Whitelist, Classify(f.Service, "github.com"));
        Assert.Equal(AccessClass.Greylist, Classify(f.Service, "new.example"));
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public MemoryStore Store { get; }
        public VaultService Service { get; }
        public Fixture() { Store = new(Clock.Now); Service = new(Store, Clock); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int seconds) { Now = Now.AddSeconds(seconds); _ticks += TimeSpan.FromSeconds(seconds).Ticks; }
        public void JumpWall(int seconds) => Now = Now.AddSeconds(seconds);
    }

    private sealed class MemoryStore(DateTimeOffset now) : IVaultStore
    {
        public VaultState Vault { get; set; } = VaultState.CreateDevelopment(now);
        private AccessStateSnapshot _access = new(now, DateTimeOffset.MinValue, []);
        private string _password = Password;
        public bool FailWrites { get; set; }
        public int Verifications { get; private set; }
        public AccessConfigurationState ConfigurationState => AccessConfigurationState.Ready;
        public AccessStateSnapshot Load() => _access;
        public void Save(AccessStateSnapshot state) { if (FailWrites) throw new IOException(); _access = state; }
        public VaultState LoadVault() => Vault;
        public void SaveVault(VaultState state, string? replacementVerifier = null)
        {
            if (FailWrites) throw new IOException();
            Vault = state;
            if (replacementVerifier is not null) _password = Replacement;
        }
        public string PreparePassword(string password) => "opaque-test-verifier";
        public bool Verify(string password) { Verifications++; return password == _password; }
        public void Initialize(string password, DateTimeOffset time) => throw new NotSupportedException();
    }
}
