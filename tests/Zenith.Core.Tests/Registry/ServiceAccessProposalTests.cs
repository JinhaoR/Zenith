using Zenith.Core.Access;
using Zenith.Core.Filtering;
using Zenith.Core.Navigation;
using Zenith.Core.Permissions;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.Core.Tests.Registry;

public sealed class ServiceAccessProposalTests
{
    internal static RelationshipProvenance Evidence(string explanation = "Reviewed browser navigation.") =>
        new(RegistrySourceType.Curated, "Fixture review", new("Test curator", DateTimeOffset.UnixEpoch), explanation);
    internal static DomainRequirement Domain(string host, bool required = true,
        PermissionApplicability applicability = PermissionApplicability.RequiredNavigation) =>
        new(host, "Browser document.", required, DependencyType.Navigation, Evidence(), applicability);
    internal static ServiceRegistry Catalog(params DomainRequirement[] domains) => new("fixture-1",
        [new("mail", "Mail", "communication", "Mail", [], domains.Length == 0 ? [Domain("mail.example")] : domains, [], ServiceStatus.Initial)], [], []);
    private static AccessProposal Build(ServiceRegistry registry, SitePolicySnapshot? policy = null, params string[] options) =>
        new ServiceAccessProposalBuilder().Build(new("mail", registry.Revision, options), registry, policy ?? new([]));

    [Fact]
    public void ProposalIdentityAndConsequencesAreDeterministicAndOrderIndependent()
    {
        var first = Catalog(Domain("b.example"), Domain("a.example"));
        var second = Catalog(Domain("a.example"), Domain("b.example"));
        var a = Build(first);
        var b = Build(second);
        Assert.Equal(a.RegistryFingerprint, b.RegistryFingerprint);
        Assert.Equal(a.ProposalId, b.ProposalId);
        Assert.Equal(new[] { "a.example", "b.example" }, a.ProposedDomains.Select(d => d.Hostname));
        Assert.All(a.ProposedDomains, d => Assert.False(d.IncludeSubdomains));
        Assert.NotEqual(a.ProposalId, Build(first, new([], 1)).ProposalId);
        var differentEvidence = new DomainRequirement("a.example", "Browser document.", true, DependencyType.Navigation,
            Evidence("Another reviewed explanation."), PermissionApplicability.RequiredNavigation);
        Assert.NotEqual(a.RegistryFingerprint, Build(Catalog(differentEvidence, Domain("b.example"))).RegistryFingerprint);
    }

    [Fact]
    public void OptionalBackgroundAndUnknownRelationshipsCannotBecomeImplicitGrants()
    {
        var registry = Catalog(Domain("mail.example"), Domain("optional.example", false, PermissionApplicability.OptionalNavigation),
            Domain("api.example", false, PermissionApplicability.BackgroundOnly),
            new("unknown.example", "Unreviewed", false, DependencyType.Authentication));
        var proposal = Build(registry);
        Assert.True(proposal.CanStage);
        Assert.Equal("mail.example", Assert.Single(proposal.ProposedDomains).Hostname);
        Assert.Equal("optional.example", Assert.Single(proposal.OptionalDomains).Hostname);
        Assert.Equal(2, Build(registry, options: ["optional.example"]).ProposedDomains.Count);
        Assert.Throws<ArgumentException>(() => Build(registry, options: ["api.example"]));
        Assert.Throws<ArgumentException>(() => Build(registry, options: ["unknown.example"]));
        Assert.Throws<ArgumentException>(() => Build(registry, options: ["google.com"]));
    }

    [Theory]
    [InlineData(DependencyType.Unknown)]
    [InlineData(DependencyType.Authentication)]
    [InlineData(DependencyType.Api)]
    public void ClassificationAndUnreviewedApplicabilityDoNotAuthorizeProposals(DependencyType type)
    {
        var registry = Catalog(new DomainRequirement("mail.example", "Unreviewed", true, type, null, PermissionApplicability.RequiredNavigation));
        var proposal = Build(registry);
        Assert.False(new ServiceAccessProposalBuilder().CanOffer(registry, "mail"));
        Assert.False(proposal.CanStage);
        Assert.Empty(proposal.ProposedDomains);
        Assert.NotEmpty(proposal.Conflicts);
        Assert.Throws<ArgumentException>(() => ServiceVaultProposal.CreateEdit(proposal));
    }

    [Fact]
    public void RequiredBlacklistConflictIsVisibleAndOptionalBlacklistIsNotSelected()
    {
        var policy = new SitePolicySnapshot([], 9, HostsBlacklist.Parse("0.0.0.0 blocked.example"));
        var blocked = Build(Catalog(Domain("blocked.example")), policy);
        Assert.Equal(ProposalAccessState.Blacklisted, blocked.ProposedDomains[0].AccessState);
        Assert.False(blocked.CanStage);
        Assert.NotEmpty(blocked.Conflicts);
        var optional = Build(Catalog(Domain("mail.example"), Domain("blocked.example", false, PermissionApplicability.OptionalNavigation)), policy);
        Assert.True(optional.CanStage);
        Assert.Equal(ProposalAccessState.Blacklisted, optional.OptionalDomains[0].AccessState);
    }

    [Fact]
    public void ExistingParentCoverageIsReferencedWithoutExpandingScopeOrInferringServiceIntent()
    {
        var proposal = Build(Catalog(Domain("mail.example")), new([new("example", AccessClass.Whitelist, true)], 4));
        var domain = Assert.Single(proposal.ProposedDomains);
        Assert.Equal(ProposalAccessState.AlreadyAllowed, domain.AccessState);
        Assert.Equal(new ExistingHostScope("example", true), Assert.Single(domain.ExistingScopes));
        Assert.Empty(ServiceVaultProposal.CreateEdit(proposal).Additions());
        Assert.Equal("mail.example", domain.Hostname);
    }

    [Fact]
    public void SharedInfrastructureIsExplanatoryAndDoesNotBlockServiceEntryPoints()
    {
        var service = new ServiceDefinition("mail", "Mail", "communication", "Mail", [], [Domain("mail.example")], [new("identity")], ServiceStatus.Initial);
        var registry = new ServiceRegistry("1", [service], [], [new("identity", "Identity", "Sign-in", [Domain("login.example")])]);
        var proposal = Build(registry);
        Assert.True(proposal.CanStage);
        Assert.Equal("mail.example", Assert.Single(proposal.ProposedDomains).Hostname);
        Assert.Empty(proposal.Conflicts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrozenServiceProposalUsesExistingAuthenticationWaitAndAtomicConfirmation(bool passwordRequired)
    {
        var f = new Fixture(passwordRequired);
        var oldRegistry = Catalog();
        var proposal = Build(oldRegistry, f.Store.Vault.ToPolicy());
        var review = f.Vault.Review(ServiceVaultProposal.CreateEdit(proposal));
        Assert.Empty(f.Store.Vault.Sites);
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        if (passwordRequired)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Stage(review.Edit, "", expectedRevision: review.Revision).Result);
            f.Clock.Advance(5);
        }
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(review.Edit, "test-password", expectedRevision: review.Revision).Result);
        var pending = f.Store.Vault.Pending!;
        Assert.Equal(VaultResult.TooEarly, f.Vault.Confirm(pending.Id, "test-password").Result);
        var updated = Build(Catalog(Domain("mail.example"), Domain("new.example")), f.Store.Vault.ToPolicy());
        Assert.NotEqual(updated.ProposalId, proposal.ProposalId);
        Assert.Equal(proposal.ProposalId, pending.Edit.ServiceProposal!.ProposalId);
        f.Clock.Advance(5);
        if (passwordRequired)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Confirm(pending.Id, "").Result);
            Assert.Empty(f.Store.Vault.ServiceApprovals);
            f.Clock.Advance(5);
        }
        f.Store.FailWrites = true;
        var unchanged = f.Store.Vault;
        Assert.Equal(VaultResult.Unavailable, f.Vault.Confirm(pending.Id, "test-password").Result);
        Assert.Same(unchanged, f.Store.Vault);
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        f.Store.FailWrites = false;
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(pending.Id, "test-password").Result);
        f.Store.Vault.Validate();
        var approval = Assert.Single(f.Store.Vault.ServiceApprovals);
        Assert.Equal(proposal.ProposalId, approval.Proposal.ProposalId);
        Assert.Equal(pending.Id, approval.VaultProposalId);
        Assert.Equal(proposal.ProposalId, Assert.Single(f.Store.Vault.Sites).ServiceOriginId);
        Assert.Equal(AccessClass.Greylist, f.Store.Vault.ToPolicy().Classify(new SitePolicyEntry("new.example", AccessClass.Whitelist).Identity));
        Assert.Equal(VaultResult.Stale, f.Vault.Confirm(pending.Id, "test-password").Result);
        Assert.All(Enum.GetValues<BrowserCapability>(), capability => Assert.False(new BrowserCapabilityPolicy().Evaluate(capability).Allowed));
    }

    [Fact]
    public void StaleForgedAndCancelledProposalsCannotStageOrApply()
    {
        var f = new Fixture();
        var proposal = Build(Catalog(), f.Store.Vault.ToPolicy());
        var edit = ServiceVaultProposal.CreateEdit(proposal);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(edit with { AddSites = [new("example", true)] }, "").Result);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(edit with { VaultSeconds = 5 }, "").Result);
        Assert.Throws<ArgumentException>(() => ServiceVaultProposal.CreateEdit(new(new("mail", "1"), [new("bypass.example", ["fake"])])));
        f.Store.Vault = f.Store.Vault with { Revision = 1 };
        Assert.Equal(VaultResult.Stale, f.Vault.Stage(edit, "", expectedRevision: 0).Result);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(edit, "").Result);
        var current = ServiceVaultProposal.CreateEdit(Build(Catalog(), f.Store.Vault.ToPolicy()));
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(current, "").Result);
        var id = f.Store.Vault.Pending!.Id;
        Assert.Equal(VaultResult.Cancelled, f.Vault.Cancel(id).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Stale, f.Vault.Confirm(id, "").Result);
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        Assert.Empty(f.Store.Vault.Sites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MandatoryBlacklistIsRecheckedForNewAndAlreadyAllowedScopes(bool alreadyAllowed)
    {
        var f = new Fixture();
        if (alreadyAllowed) f.Store.Vault = VaultPermissionLedger.MigrateLegacy(f.Store.Vault with { Sites = [new("mail.example", AccessClass.Whitelist, false)] });
        var list = new BlacklistSource();
        var vault = new VaultService(f.Store, f.Clock, list);
        Assert.True(vault.TryGetActivePolicy(out var policy));
        var edit = ServiceVaultProposal.CreateEdit(Build(Catalog(), policy));
        Assert.Equal(VaultResult.Staged, vault.Stage(edit, "").Result);
        var id = f.Store.Vault.Pending!.Id;
        list.Current = HostsBlacklist.Parse("0.0.0.0 mail.example");
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Unavailable, vault.Confirm(id, "").Result);
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        Assert.Throws<ArgumentException>(() => vault.Review(edit));
        Assert.Equal(VaultResult.Invalid, vault.Stage(edit, "").Result);
    }

    [Fact]
    public void SharedScopesKeepBothAttributionsAndManualEditsNeverResurrectAccess()
    {
        var f = new Fixture();
        ServiceRegistry Shared(string id) => new("1", [new(id, id, "communication", "Service", [],
            [Domain(id + ".example"), Domain("login.example")], [new("identity", Evidence())], ServiceStatus.Initial)], [],
            [new("identity", "Identity", "Sign-in", [Domain("login.example")])]);
        foreach (var id in new[] { "first", "second" })
        {
            var proposal = new ServiceAccessProposalBuilder().Build(new(id, "1"), Shared(id), f.Store.Vault.ToPolicy());
            Assert.Equal(VaultResult.Staged, f.Vault.Stage(ServiceVaultProposal.CreateEdit(proposal), "").Result);
            f.Clock.Advance(5);
            Assert.Equal(VaultResult.Applied, f.Vault.Confirm(f.Store.Vault.Pending!.Id, "").Result);
        }
        Assert.Equal(2, f.Store.Vault.ServiceApprovals.Count);
        Assert.All(f.Store.Vault.ServiceApprovals, a => Assert.Contains(a.Proposal.ProposedDomains, d => d.Hostname == "login.example"));
        Assert.Equal(ProposalAccessState.AlreadyAllowed, f.Store.Vault.ServiceApprovals[1].Proposal.ProposedDomains.Single(d => d.Hostname == "login.example").AccessState);
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(new(AddSites: [], RemoveSites: ["login.example"]), "").Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(f.Store.Vault.Pending!.Id, "").Result);
        Assert.Equal(2, f.Store.Vault.ServiceApprovals.Count);
        Assert.DoesNotContain(f.Store.Vault.Sites, s => s.Host == "login.example");
        f.Store.Vault.Validate();
    }

    [Fact]
    public void ExplicitApprovalOfExistingManualAccessStillWaitsAndPreservesIndependentOrigin()
    {
        var f = new Fixture();
        f.Store.Vault = VaultPermissionLedger.MigrateLegacy(f.Store.Vault with { Sites = [new("mail.example", AccessClass.Whitelist, false)] });
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        var proposal = Build(Catalog(), f.Store.Vault.ToPolicy());
        var edit = ServiceVaultProposal.CreateEdit(proposal);
        Assert.Empty(edit.Additions());
        Assert.Empty(f.Store.Vault.ServiceApprovals);
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(edit, "").Result);
        var id = f.Store.Vault.Pending!.Id;
        Assert.Equal(VaultResult.TooEarly, f.Vault.Confirm(id, "").Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(id, "").Result);
        Assert.Null(Assert.Single(f.Store.Vault.Sites).ServiceOriginId);
        Assert.Equal(ProposalAccessState.AlreadyAllowed, Assert.Single(f.Store.Vault.ServiceApprovals).Proposal.ProposedDomains[0].AccessState);
    }

    private sealed class Fixture(bool passwordRequired = false)
    {
        public TestClock Clock { get; } = new();
        public TestStore Store { get; } = new(passwordRequired);
        public VaultService Vault => new(Store, Clock);
    }
    internal sealed class TestClock : TimeProvider
    {
        private long _seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(_seconds);
        public void Advance(int seconds) => _seconds += seconds;
    }
    internal sealed class TestStore(bool passwordRequired) : IVaultStore
    {
        public VaultState Vault { get; set; } = VaultState.CreateDevelopment(DateTimeOffset.UnixEpoch) with { Sites = [], PasswordRequired = passwordRequired };
        public bool FailWrites { get; set; }
        public bool PasswordRequired => Vault.PasswordRequired;
        public AccessConfigurationState ConfigurationState => AccessConfigurationState.Ready;
        public AccessStateSnapshot Load() => new(Vault.LastObservedUtc, DateTimeOffset.MinValue, []);
        public void Save(AccessStateSnapshot state) => throw new NotSupportedException();
        public VaultState LoadVault() => Vault;
        public void SaveVault(VaultState state, string? replacementVerifier = null) { state.Validate(); if (FailWrites) throw new IOException(); Vault = state; }
        public string PreparePassword(string password) => throw new NotSupportedException();
        public bool Verify(string password) => password == "test-password";
        public void Initialize(string password, DateTimeOffset now) => throw new NotSupportedException();
    }
    private sealed class BlacklistSource : IBlacklistSource
    { public HostsBlacklist? Current { get; set; } = HostsBlacklist.Parse("0.0.0.0 other.example"); }
}
