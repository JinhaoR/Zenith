using Zenith.Core.Filtering;
using Zenith.Core.Navigation;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.Core.Tests.Registry;

public sealed class InfrastructureAndLocalExtensionTests
{
    private static DomainRequirement Domain(string host) => ServiceAccessProposalTests.Domain(host);
    internal static ServiceRegistry Catalog(params string[] infrastructureHosts) => new("baseline-1",
        [new("mail", "Mail", "communication", "Mail service", [], [Domain("mail.example")], [new("identity")], ServiceStatus.Initial)], [],
        [new("identity", "Identity", "Shared sign-in", (infrastructureHosts.Length == 0 ? ["login.example"] : infrastructureHosts).Select(Domain))]);

    [Fact]
    public void NewServiceApprovalsDoNotExpandOrRequireSharedInfrastructure()
    {
        var registry = Catalog();
        var policy = new SitePolicySnapshot([], 0, HostsBlacklist.Parse("0.0.0.0 login.example"));
        var proposal = new ServiceAccessProposalBuilder().Build(new("mail", registry.Revision), registry, policy);
        Assert.True(proposal.CanStage);
        Assert.Equal("mail.example", Assert.Single(proposal.ProposedDomains).Hostname);
        Assert.Null(Assert.Single(proposal.ProposedDomains[0].Relationships).InfrastructureId);
        Assert.Throws<ArgumentException>(() => InfrastructureBaselineProposal.Prepare(registry, policy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaselineNeedsExistingVaultAuthenticationWaitAndExplicitConfirmation(bool password)
    {
        var f = new Fixture(password);
        var proposal = InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy());
        var review = f.Vault.Review(RegistryVaultProposal.CreateEdit(proposal));
        f.Denied("login.example");
        if (password)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Stage(review.Edit, "").Result);
            f.Clock.Advance(5);
        }
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(review.Edit, f.Password).Result);
        var id = f.State.Pending!.Id;
        Assert.Equal(VaultResult.TooEarly, f.Vault.Confirm(id, f.Password).Result);
        f.Denied("login.example");
        f.Clock.Advance(5);
        f.Denied("login.example");
        if (password)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Confirm(id, "").Result);
            f.Clock.Advance(5);
        }
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(id, f.Password).Result);
        Assert.IsType<NavigationDecision.Allowed>(f.Navigate("login.example"));
        Assert.False(Assert.Single(f.State.Sites).IncludeSubdomains);
        Assert.Single(f.State.InfrastructureActivations);
        Assert.Empty(f.State.ServiceApprovals);
        foreach (var host in new[] { "example", "child.login.example", "www.login.example", "other.example" }) f.Denied(host);
        Assert.Equal(new[] { "login.example" }, f.State.GetInfrastructureCreatedHosts());
    }

    [Fact]
    public void CatalogAdditionsAndRemovalsCannotModifyFrozenBaselineOrActivePermissions()
    {
        var f = new Fixture();
        var proposal = InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy());
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(RegistryVaultProposal.CreateEdit(proposal), "").Result);
        var changed = Catalog("login.example", "new.example");
        Assert.NotEqual(proposal.ProposalId, InfrastructureBaselineProposal.Prepare(changed, f.State.ToPolicy()).ProposalId);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(f.State.Pending!.Id, "").Result);
        f.Denied("new.example");
        _ = Catalog("replacement.example");
        Assert.IsType<NavigationDecision.Allowed>(f.Navigate("login.example"));
        f.Apply(new(RemoveHost: "login.example"));
        Assert.Single(f.State.InfrastructureActivations);
        f.Denied("login.example");
        f.Apply(new(AddHost: "login.example"));
        Assert.Empty(f.State.GetInfrastructureCreatedHosts());
    }

    [Fact]
    public void BaselineNeverHidesExistingManualPermissionOrCreatesRedundantScope()
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "login.example"));
        var id = Assert.Single(f.State.Sites).PermissionInstanceId;
        var baseline = InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy());
        Assert.Empty(baseline.NewHostnames);
        f.Apply(RegistryVaultProposal.CreateEdit(baseline));
        Assert.Equal(id, Assert.Single(f.State.Sites).PermissionInstanceId);
        Assert.Empty(f.State.GetInfrastructureCreatedHosts());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlacklistIsRecheckedEvenWhenBaselineHostAlreadyHasAccess(bool alreadyAllowed)
    {
        var f = new Fixture();
        if (alreadyAllowed) f.Apply(new(AddHost: "login.example"));
        var edit = RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy()));
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(edit, "").Result);
        f.Blacklist.Current = HostsBlacklist.Parse("0.0.0.0 login.example");
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Unavailable, f.Vault.Confirm(f.State.Pending!.Id, "").Result);
        Assert.Throws<ArgumentException>(() => f.Vault.Review(edit));
        Assert.Empty(f.State.InfrastructureActivations);
        Assert.Equal(NavigationDenialReason.Blacklisted, Assert.IsType<NavigationDecision.Denied>(f.Navigate("login.example")).Reason);
    }

    [Fact]
    public void UnreviewedAndBackgroundInfrastructureCannotBecomeBaselinePermissions()
    {
        var registry = new ServiceRegistry("1", [], [], [new("identity", "Identity", "Description", [
            new("login.example", "Unreviewed sign-in", true, DependencyType.Authentication, permissionApplicability: PermissionApplicability.RequiredNavigation),
            new("api.example", "API", true, DependencyType.Api, permissionApplicability: PermissionApplicability.BackgroundOnly)])]);
        Assert.Throws<ArgumentException>(() => InfrastructureBaselineProposal.Prepare(registry, new([])));
        var background = new ServiceRegistry("1", [], [], [new("api", "API", "Background", [
            new("api.example", "API", true, DependencyType.Api, permissionApplicability: PermissionApplicability.BackgroundOnly)])]);
        Assert.Throws<ArgumentException>(() => InfrastructureBaselineProposal.Prepare(background, new([])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalExceptionIsExplicitExactAndRemovableWithoutChangingGlobalCatalog(bool password)
    {
        var f = new Fixture(password);
        var registry = Catalog();
        Assert.Throws<ArgumentException>(() => LocalServiceExtensionProposal.Prepare(registry, "mail", "sso.university.example", "University sign-in", f.State.ToPolicy()));
        f.Apply(new(AddHost: "mail.example"));
        var fingerprint = RegistryContentIdentity.Registry(registry);
        var proposal = LocalServiceExtensionProposal.Prepare(registry, "mail", "sso.university.example", "University sign-in", f.State.ToPolicy());
        var edit = RegistryVaultProposal.CreateEdit(proposal, f.State.ToPolicy());
        if (password)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Stage(edit, "").Result);
            f.Clock.Advance(5);
        }
        f.Denied(proposal.Hostname);
        f.Apply(edit);
        Assert.IsType<NavigationDecision.Allowed>(f.Navigate(proposal.Hostname));
        f.Denied("child.sso.university.example");
        Assert.Equal(fingerprint, RegistryContentIdentity.Registry(registry));
        Assert.Empty(registry.FindServicesAssociatedWithHostname(proposal.Hostname));
        var extension = Assert.Single(f.State.GetActiveLocalExtensions());
        Assert.Equal(proposal.ProposalId, extension.Proposal.ProposalId);
        f.Apply(new(RemoveHost: proposal.Hostname));
        f.Denied(proposal.Hostname);
        Assert.Empty(f.State.GetActiveLocalExtensions());
        Assert.Single(f.State.LocalServiceExtensions); // history cannot restore access
        f.Apply(new(AddHost: proposal.Hostname));
        Assert.Empty(f.State.GetActiveLocalExtensions()); // no reattachment to a new permission
    }

    [Fact]
    public void LocalLabelsCanCoexistWithoutOwningAHost()
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "mail.example"));
        foreach (var id in new[] { "mail", "work" })
        {
            var proposal = new LocalServiceExtensionProposal(id, id, "mail.example", "login.example", "Institution sign-in", f.State.Revision);
            f.Apply(RegistryVaultProposal.CreateEdit(proposal, f.State.ToPolicy()));
        }
        Assert.Equal(2, f.State.GetActiveLocalExtensions().Count);
        Assert.Single(f.State.Sites, s => s.Host == "login.example");
        Assert.Single(f.State.GetActiveLocalExtensions().Select(e => e.PermissionInstanceId).Distinct());
        f.Apply(new(RemoveHost: "login.example"));
        f.Denied("login.example");
        Assert.Empty(f.State.GetActiveLocalExtensions());
    }

    [Theory]
    [InlineData("mail.example")]
    [InlineData("login.example")]
    public void LocalExceptionRechecksBlacklistForContextAndDestination(string blocked)
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "mail.example"));
        var local = LocalServiceExtensionProposal.Prepare(Catalog(), "mail", "login.example", "Sign-in", f.State.ToPolicy());
        var edit = RegistryVaultProposal.CreateEdit(local, f.State.ToPolicy());
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(edit, "").Result);
        f.Blacklist.Current = HostsBlacklist.Parse("0.0.0.0 " + blocked);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Unavailable, f.Vault.Confirm(f.State.Pending!.Id, "").Result);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(edit, "").Result);
        Assert.Empty(f.State.LocalServiceExtensions);
        f.Denied("login.example");
    }

    [Theory]
    [InlineData("*.university.example")]
    [InlineData("https://login.university.example/path")]
    [InlineData("login.university.example:443")]
    public void LocalExceptionsRejectWildcardAndNonHostnameInput(string host)
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "mail.example"));
        Assert.Throws<ArgumentException>(() => LocalServiceExtensionProposal.Prepare(Catalog(), "mail", host, "Sign-in", f.State.ToPolicy()));
        Assert.Null(f.State.Pending);
        Assert.Empty(f.State.LocalServiceExtensions);
    }

    [Fact]
    public void BroaderCoverageAndStaleOrMixedProposalsDoNotProduceNewExceptions()
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "example", IncludeSubdomains: true));
        var local = LocalServiceExtensionProposal.Prepare(Catalog(), "mail", "login.example", "Sign-in", f.State.ToPolicy());
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(RegistryVaultProposal.CreateEdit(local, f.State.ToPolicy()), "").Result);
        var baseline = RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy()));
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(baseline with { LocalExtensionProposal = local }, "").Result);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(baseline with { AddSites = [new("example", true)] }, "").Result);
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(baseline with { VaultSeconds = 5 }, "").Result);
        f.Apply(new(AddHost: "unrelated.test"));
        Assert.Equal(VaultResult.Invalid, f.Vault.Stage(baseline, "").Result);
    }

    [Fact]
    public void ContextIsAdvisoryAndNeverCreatesAPermissionOrProposal()
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "mail.example"));
        var state = f.State;
        var suggestion = UnknownAuthenticationSuggestion.Create(Catalog(), state.ToPolicy(), "mail.example", "evil.example", f.Navigate("evil.example"));
        Assert.NotNull(suggestion);
        Assert.Contains("may be related", suggestion.Explanation);
        Assert.Contains("has not reviewed", suggestion.Explanation);
        Assert.DoesNotContain("requires", suggestion.Explanation);
        Assert.Same(state, f.State);
        Assert.Null(f.State.Pending);
        Assert.Empty(f.State.LocalServiceExtensions);
        f.Denied("evil.example");
        Assert.Null(UnknownAuthenticationSuggestion.Create(Catalog(), state.ToPolicy(), "mail.example", "evil.example?token=secret", f.Navigate("evil.example")));
        Assert.Null(UnknownAuthenticationSuggestion.Create(Catalog(), state.ToPolicy(), "unknown.example", "evil.example", f.Navigate("evil.example")));
        Assert.Null(UnknownAuthenticationSuggestion.Create(Catalog(), state.ToPolicy(), "mail.example", "login.example", f.Navigate("login.example")));
        Assert.Null(UnknownAuthenticationSuggestion.Create(Catalog(), state.ToPolicy(), "mail.example", "evil.example", new NavigationDecision.Denied(NavigationDenialReason.Blacklisted)));
        var service = Catalog().Services[0];
        var ambiguous = new ServiceRegistry("2", [service, new("other", "Other", "communication", "Shared endpoint", [], service.Domains, [], ServiceStatus.Initial)], [], Catalog().Infrastructure);
        Assert.Null(UnknownAuthenticationSuggestion.Create(ambiguous, state.ToPolicy(), "mail.example", "evil.example", f.Navigate("evil.example")));
    }

    [Fact]
    public void FailedWriteLeavesFrozenBaselineAndPermissionsUnchanged()
    {
        var f = new Fixture();
        var edit = RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(Catalog(), f.State.ToPolicy()));
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(edit, "").Result);
        f.Clock.Advance(5);
        var before = f.State;
        f.Store.FailWrites = true;
        Assert.Equal(VaultResult.Unavailable, f.Vault.Confirm(before.Pending!.Id, "").Result);
        Assert.Same(before, f.State);
        f.Denied("login.example");
        f.Store.FailWrites = false;
        Assert.Equal(VaultResult.Cancelled, f.Vault.Cancel(before.Pending.Id).Result);
        Assert.Equal(VaultResult.Stale, f.Vault.Confirm(before.Pending.Id, "").Result);
    }

    private sealed class Fixture(bool password = false)
    {
        public ServiceAccessProposalTests.TestClock Clock { get; } = new();
        public ServiceAccessProposalTests.TestStore Store { get; } = new(password);
        public BlacklistSource Blacklist { get; } = new();
        public VaultState State => Store.Vault;
        public VaultService Vault => new(Store, Clock, Blacklist);
        public string Password => password ? "test-password" : "";
        public NavigationDecision Navigate(string host) => new SitePolicyNavigationEvaluator(Vault).Evaluate(new("https://" + host, NavigationOrigin.WebView));
        public void Denied(string host) => Assert.IsType<NavigationDecision.Denied>(Navigate(host));
        public void Apply(VaultEdit edit)
        {
            Assert.Equal(VaultResult.Staged, Vault.Stage(edit, Password).Result);
            var id = State.Pending!.Id;
            Assert.Equal(VaultResult.TooEarly, Vault.Confirm(id, Password).Result);
            Clock.Advance(5);
            Assert.Equal(VaultResult.Applied, Vault.Confirm(id, Password).Result);
        }
    }
    private sealed class BlacklistSource : IBlacklistSource
    { public HostsBlacklist? Current { get; set; } = HostsBlacklist.Parse("0.0.0.0 blocked.example"); }
}
