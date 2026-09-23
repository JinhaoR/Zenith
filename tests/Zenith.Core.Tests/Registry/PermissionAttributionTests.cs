using Zenith.Core.Access;
using Zenith.Core.Filtering;
using Zenith.Core.Navigation;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.Core.Tests.Registry;

public sealed class PermissionAttributionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdentityAndManualAttributionAreCreatedOnlyByConfirmedVaultChange(bool passwordRequired)
    {
        var f = new Fixture(passwordRequired);
        var edit = f.Vault.Review(new(AddHost: "login.example")).Edit;
        Assert.Empty(f.Store.Vault.PermissionInstances);
        if (passwordRequired)
        {
            Assert.Equal(VaultResult.WrongPassword, f.Vault.Stage(edit, "wrong").Result);
            Assert.Empty(f.Store.Vault.PermissionAttributions);
            f.Clock.Advance(5);
        }
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(edit, f.Password).Result);
        Assert.Empty(f.Store.Vault.PermissionInstances);
        var operation = f.Store.Vault.Pending!.Id;
        Assert.Equal(VaultResult.TooEarly, f.Vault.Confirm(operation, f.Password).Result);
        f.Clock.Advance(5);
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(operation, f.Password).Result);
        var permission = Assert.Single(f.Store.Vault.PermissionInstances);
        Assert.NotEqual(Guid.Empty, permission.Id);
        Assert.Equal(permission.Id, Assert.Single(f.Store.Vault.Sites).PermissionInstanceId);
        var attribution = Assert.Single(f.Store.Vault.PermissionAttributions);
        Assert.Equal(PermissionAttributionSource.ManualVaultChange, attribution.Source);
        Assert.Equal(operation, attribution.OriginatingVaultOperationId);
        Assert.Equal(1, attribution.RecordedPolicyRevision);
        Assert.Null(attribution.ServiceApprovalId);
        Assert.Null(attribution.Requirement);
        f.Apply(new(GreylistSeconds: 10));
        Assert.Equal(permission, Assert.Single(f.Store.Vault.PermissionInstances));
        Assert.Equal(attribution, Assert.Single(f.Store.Vault.PermissionAttributions));
    }

    [Fact]
    public void RemovalRecreationAndScopeReplacementNeverReuseOrRebindAnOldIdentity()
    {
        var f = new Fixture();
        f.Approve("mail");
        var first = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        var serviceAttribution = Assert.Single(f.Store.Vault.PermissionAttributions);
        f.Apply(new(RemoveHost: "login.example"));
        Assert.Empty(f.Store.Vault.Sites);
        Assert.Single(f.Store.Vault.PermissionInstances);
        Assert.Equal(serviceAttribution, Assert.Single(f.Store.Vault.PermissionAttributions));
        Assert.IsType<NavigationDecision.Denied>(f.Navigate("login.example"));

        f.Apply(new(AddHost: "login.example"));
        var second = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        Assert.NotEqual(first, second);
        Assert.Equal(PermissionAttributionSource.ManualVaultChange,
            Assert.Single(f.Store.Vault.PermissionAttributions, a => a.PermissionInstanceId == second).Source);
        Assert.Equal(first, serviceAttribution.PermissionInstanceId);

        f.Apply(new(AddHost: "login.example", IncludeSubdomains: true));
        var third = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        Assert.NotEqual(second, third);
        f.Apply(new(AddHost: "login.example", IncludeSubdomains: false));
        var fourth = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        Assert.Equal(4, new[] { first, second, third, fourth }.Distinct().Count());
        Assert.Equal(4, f.Store.Vault.PermissionInstances.Count);
        Assert.IsType<NavigationDecision.Denied>(f.Navigate("child.login.example"));
    }

    [Fact]
    public void AnOldApprovalCannotBeBoundToALaterInstanceOfTheSameHostname()
    {
        var f = new Fixture();
        f.Approve("mail");
        var old = Assert.Single(f.Store.Vault.PermissionAttributions);
        f.Apply(new(RemoveHost: "login.example"));
        f.Apply(new(AddHost: "login.example"));
        var later = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        var invalid = f.Store.Vault with { PermissionAttributions = f.Store.Vault.PermissionAttributions
            .Append(old with { PermissionInstanceId = later }).ToArray() };
        Assert.Throws<InvalidOperationException>(() => invalid.Validate());
    }

    [Fact]
    public void TwoServicesAndManualContributionCoexistOnOnePermission()
    {
        var f = new Fixture();
        var manual = f.Apply(new(AddHost: "login.example"));
        var id = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        var gmail = f.Approve("gmail");
        var drive = f.Approve("drive");
        Assert.Single(f.Store.Vault.PermissionInstances);
        Assert.Equal(id, Assert.Single(f.Store.Vault.Sites).PermissionInstanceId);
        Assert.Equal(3, f.Store.Vault.PermissionAttributions.Count);
        Assert.All(f.Store.Vault.PermissionAttributions, a => Assert.Equal(id, a.PermissionInstanceId));
        Assert.Contains(f.Store.Vault.PermissionAttributions, a => a.OriginatingVaultOperationId == manual && a.Requirement is null);
        foreach (var approvalId in new[] { gmail, drive })
        {
            var attribution = Assert.Single(f.Store.Vault.PermissionAttributions, a => a.ServiceApprovalId == approvalId);
            var approval = f.Store.Vault.ServiceApprovals.Single(a => a.VaultProposalId == approvalId);
            Assert.Equal(approvalId, attribution.OriginatingVaultOperationId);
            var reference = attribution.Requirement!;
            var requirement = approval.Proposal.ProposedDomains.Single(d => d.Hostname == reference.Hostname)
                .Relationships[reference.RelationshipIndex];
            Assert.Equal("identity", requirement.InfrastructureId);
            Assert.Equal("login.example", requirement.Requirement.Hostname);
            Assert.NotNull(requirement.InfrastructureProvenance!.Review);
        }
    }

    [Fact]
    public void EveryReviewedRelationshipHasSeparateAttributionEvenWhenHostnameIsShared()
    {
        var f = new Fixture();
        var registry = Catalog("mail", directRelationship: true);
        f.Approve("mail", registry);
        var attrs = f.Store.Vault.PermissionAttributions;
        Assert.Equal(2, attrs.Count);
        Assert.Equal(new[] { 0, 1 }, attrs.Select(a => a.Requirement!.RelationshipIndex));
        Assert.Single(f.Store.Vault.Sites);
    }

    [Fact]
    public void ExistingBroadCoverageIsBoundWithoutCreatingChildPermissionOrTransferringItsAttribution()
    {
        var f = new Fixture();
        f.Apply(new(AddHost: "example", IncludeSubdomains: true));
        var parent = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        f.Approve("mail");
        Assert.All(f.Store.Vault.PermissionAttributions, a => Assert.Equal(parent, a.PermissionInstanceId));
        Assert.Equal("login.example", f.Store.Vault.PermissionAttributions.Single(a => a.Requirement is not null).Requirement!.Hostname);
        f.Apply(new(AddHost: "example", IncludeSubdomains: false));
        Assert.IsType<NavigationDecision.Denied>(f.Navigate("login.example"));
        Assert.Single(f.Store.Vault.ServiceApprovals);
        var replacement = Assert.Single(f.Store.Vault.Sites).PermissionInstanceId;
        Assert.NotEqual(parent, replacement);
        Assert.All(f.Store.Vault.PermissionAttributions.Where(a => a.Requirement is not null), a => Assert.Equal(parent, a.PermissionInstanceId));
    }

    [Fact]
    public void ParentConsolidationPreservesHistoricalChildrenAndUnrelatedPermissionIdentities()
    {
        var f = new Fixture();
        f.Approve("mail");
        f.Apply(new(AddHost: "unrelated.test"));
        var child = f.Store.Vault.Sites.Single(s => s.Host == "login.example").PermissionInstanceId;
        var unrelated = f.Store.Vault.Sites.Single(s => s.Host == "unrelated.test").PermissionInstanceId;
        f.Apply(new(AddHost: "example", IncludeSubdomains: true));
        Assert.DoesNotContain(f.Store.Vault.Sites, s => s.PermissionInstanceId == child);
        Assert.Contains(f.Store.Vault.PermissionInstances, p => p.Id == child);
        Assert.Contains(f.Store.Vault.PermissionAttributions, a => a.PermissionInstanceId == child);
        Assert.Equal(unrelated, f.Store.Vault.Sites.Single(s => s.Host == "unrelated.test").PermissionInstanceId);
        Assert.Throws<ArgumentException>(() => f.Vault.Review(new(AddHost: "login.example")));
        Assert.Throws<ArgumentException>(() => f.Vault.Review(new(AddSites: [new("unrelated.test")], RemoveSites: ["unrelated.test"])));
    }

    [Fact]
    public void ChildThenParentInOneManualBatchRecordsOnlyTheCommittedParent()
    {
        var f = new Fixture();
        var operation = f.Apply(new(AddSites: [new("login.example"), new("example", true)], RemoveSites: []));
        var site = Assert.Single(f.Store.Vault.Sites);
        Assert.Equal("example", site.Host);
        Assert.True(site.IncludeSubdomains);
        Assert.Equal(site.PermissionInstanceId, Assert.Single(f.Store.Vault.PermissionInstances).Id);
        var attribution = Assert.Single(f.Store.Vault.PermissionAttributions);
        Assert.Equal(site.PermissionInstanceId, attribution.PermissionInstanceId);
        Assert.Equal(operation, attribution.OriginatingVaultOperationId);
        Assert.IsType<NavigationDecision.Allowed>(f.Navigate("login.example"));
    }

    [Fact]
    public void LegacyMigrationDoesNotInferServiceBindingsFromHistoryOrHostnameOverlap()
    {
        var f = new Fixture();
        f.Approve("mail");
        var old = f.Store.Vault;
        var legacy = old with { Sites = old.Sites.Select(s => s with { PermissionInstanceId = Guid.Empty }).ToArray(),
            PermissionInstances = [], PermissionAttributions = [] };
        var migrated = VaultPermissionLedger.MigrateLegacy(legacy);
        Assert.Equal(old.Revision, migrated.Revision);
        Assert.Same(old.ServiceApprovals, migrated.ServiceApprovals);
        Assert.Equal(old.Sites[0].ServiceOriginId, migrated.Sites[0].ServiceOriginId);
        var attr = Assert.Single(migrated.PermissionAttributions);
        Assert.Equal(PermissionAttributionSource.LegacyMigration, attr.Source);
        Assert.Null(attr.ServiceApprovalId);
        Assert.Null(attr.OriginatingVaultOperationId);
        Assert.Null(attr.Requirement);
        Assert.Equal(old.ToPolicy().Entries, migrated.ToPolicy().Entries);
    }

    [Fact]
    public void HistoricalAttributionCannotAuthorizeOrOverrideMandatoryBlacklist()
    {
        var f = new Fixture();
        f.Approve("mail");
        f.Blacklist.Current = HostsBlacklist.Parse("0.0.0.0 login.example");
        Assert.Equal(NavigationDenialReason.Blacklisted, Assert.IsType<NavigationDecision.Denied>(f.Navigate("login.example")).Reason);
        var before = f.Store.Vault;
        Assert.Throws<ArgumentException>(() => f.Vault.Review(ServiceVaultProposal.CreateEdit(new ServiceAccessProposalBuilder()
            .Build(new("second", "fixture"), Catalog("second"), before.ToPolicy()))));
        Assert.Same(before, f.Store.Vault);
        f.Apply(new(RemoveHost: "login.example"));
        f.Blacklist.Current = HostsBlacklist.Parse("0.0.0.0 other.test");
        Assert.Equal(NavigationDenialReason.Greylisted, Assert.IsType<NavigationDecision.Denied>(f.Navigate("login.example")).Reason);
        Assert.Single(f.Store.Vault.PermissionAttributions);
    }

    [Theory]
    [InlineData("permission_id")]
    [InlineData("instance_scope")]
    [InlineData("approval_id")]
    [InlineData("requirement")]
    [InlineData("duplicate")]
    [InlineData("operation")]
    [InlineData("source")]
    public void InvalidAttributionCannotBecomeReadablePolicy(string damage)
    {
        var f = new Fixture();
        f.Approve("mail");
        var state = f.Store.Vault;
        var a = Assert.Single(state.PermissionAttributions);
        f.Store.Vault = damage switch
        {
            "permission_id" => state with { Sites = [state.Sites[0] with { PermissionInstanceId = Guid.NewGuid() }] },
            "instance_scope" => state with { PermissionInstances = [state.PermissionInstances[0] with { IncludeSubdomains = true }] },
            "approval_id" => state with { PermissionAttributions = [a with { ServiceApprovalId = Guid.NewGuid() }] },
            "requirement" => state with { PermissionAttributions = [a with { Requirement = new("login.example", 20) }] },
            "duplicate" => state with { PermissionAttributions = [a, a] },
            "operation" => state with { PermissionAttributions = [a with { OriginatingVaultOperationId = Guid.NewGuid() }] },
            _ => state with { PermissionAttributions = [a with { Source = (PermissionAttributionSource)99 }] }
        };
        Assert.Throws<InvalidOperationException>(() => f.Store.Vault.Validate());
        Assert.False(f.Vault.TryGetActivePolicy(out _));
    }

    [Fact]
    public void FailedConfirmationCannotPublishPartialIdentityOrAttribution()
    {
        var f = new Fixture();
        var registry = Catalog("mail");
        var proposal = LegacyProposal(registry, "mail", f.Store.Vault.ToPolicy());
        Assert.Equal(VaultResult.Staged, f.Vault.Stage(ServiceVaultProposal.CreateEdit(proposal), "").Result);
        f.Clock.Advance(5);
        var before = f.Store.Vault;
        f.Store.FailWrites = true;
        Assert.Equal(VaultResult.Unavailable, f.Vault.Confirm(before.Pending!.Id, "").Result);
        Assert.Same(before, f.Store.Vault);
        Assert.Empty(f.Store.Vault.Sites);
        Assert.Empty(f.Store.Vault.PermissionInstances);
        Assert.Empty(f.Store.Vault.PermissionAttributions);
        f.Store.FailWrites = false;
        Assert.Equal(VaultResult.Applied, f.Vault.Confirm(before.Pending.Id, "").Result);
        Assert.Single(f.Store.Vault.PermissionAttributions);
    }

    private static ServiceRegistry Catalog(string serviceId, bool directRelationship = false) => new("fixture",
        [new(serviceId, serviceId, "communication", "Test service", [],
            directRelationship ? [ServiceAccessProposalTests.Domain("login.example")] :
                [new("api.example", "Background API", true, DependencyType.Api, permissionApplicability: PermissionApplicability.BackgroundOnly)],
            [new("identity", ServiceAccessProposalTests.Evidence())], ServiceStatus.Initial)], [],
        [new("identity", "Identity", "Test sign-in", [ServiceAccessProposalTests.Domain("login.example")])]);

    // Compatibility fixture for already-reviewed Phase 3/4A proposals. New builders
    // must not generate this expansion, but old frozen records remain valid.
    private static AccessProposal LegacyProposal(ServiceRegistry registry, string serviceId, SitePolicySnapshot policy)
    {
        var domains = registry.GetDomainRequirements(serviceId)
            .Select(a => new ProposalRelationship(a.ServiceId, a.InfrastructureId, a.Domain, a.InfrastructureProvenance))
            .Where(ServiceAccessProposalBuilder.IsEligible).GroupBy(r => r.Requirement.Hostname).Select(g =>
            {
                var identity = new SitePolicyEntry(g.Key, AccessClass.Whitelist).Identity;
                var state = policy.Classify(identity) == AccessClass.Whitelist ? ProposalAccessState.AlreadyAllowed : ProposalAccessState.NewAccess;
                var scopes = state == ProposalAccessState.AlreadyAllowed ? policy.Entries.Where(e => e.AccessClass == AccessClass.Whitelist && e.Matches(identity))
                    .Select(e => new ExistingHostScope(e.Identity.Host, e.IncludeSubdomains)).ToArray() : [];
                return new ProposedDomain(g.Key, ["Reviewed legacy requirement"], g.ToArray(), state, scopes);
            }).ToArray();
        return new(new(serviceId, registry.Revision), domains, RegistryContentIdentity.Registry(registry), policy.Revision, registry.FindService(serviceId)!.Name);
    }

    private sealed class Fixture(bool passwordRequired = false)
    {
        public ServiceAccessProposalTests.TestClock Clock { get; } = new();
        public ServiceAccessProposalTests.TestStore Store { get; } = new(passwordRequired)
        { Vault = new(0, new(), [], DateTimeOffset.UnixEpoch, DateTimeOffset.MinValue, PasswordRequired: passwordRequired) };
        public BlacklistSource Blacklist { get; } = new();
        public string Password => passwordRequired ? "test-password" : "";
        public VaultService Vault => new(Store, Clock, Blacklist);
        public NavigationDecision Navigate(string host) => new SitePolicyNavigationEvaluator(Vault).Evaluate(new("https://" + host, NavigationOrigin.WebView));
        public Guid Apply(VaultEdit edit)
        {
            Assert.Equal(VaultResult.Staged, Vault.Stage(edit, Password).Result);
            var id = Store.Vault.Pending!.Id;
            Clock.Advance(5);
            Assert.Equal(VaultResult.Applied, Vault.Confirm(id, Password).Result);
            return id;
        }
        public Guid Approve(string service, ServiceRegistry? registry = null)
        {
            registry ??= Catalog(service);
            return Apply(ServiceVaultProposal.CreateEdit(LegacyProposal(registry, service, Store.Vault.ToPolicy())));
        }
    }
    private sealed class BlacklistSource : IBlacklistSource
    { public HostsBlacklist? Current { get; set; } = HostsBlacklist.Parse("0.0.0.0 blocked.test"); }
}
