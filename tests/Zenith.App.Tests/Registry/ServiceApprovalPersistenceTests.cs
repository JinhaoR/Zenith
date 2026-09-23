using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.App.Access;
using Zenith.App.Registry;
using Zenith.Core.Access;
using Zenith.Core.Navigation;
using Zenith.Core.Registry;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Registry;

public sealed class ServiceApprovalPersistenceTests
{
    [Fact]
    public async Task FrozenProposalAndAttributionSurviveRestartWithoutRegistryAndNeverExpand()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-ServicePersistence-");
        var clock = new Clock();
        var catalog = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
        string? identity;
        Guid vaultProposalId;
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.InitializeWithoutPassword(clock.GetUtcNow());
                store.SaveVault(store.LoadVault() with { Sites = [] });
                var vault = new VaultService(store, clock);
                var proposal = new ServiceAccessProposalBuilder().Build(new("gmail", catalog.Revision), catalog, store.LoadVault().ToPolicy());
                Assert.True(proposal.CanStage);
                Assert.Equal(new[] { "mail.google.com" }, proposal.ProposedDomains.Select(d => d.Hostname));
                identity = proposal.ProposalId;
                var review = vault.Review(ServiceVaultProposal.CreateEdit(proposal));
                Assert.Equal(VaultResult.Staged, vault.Stage(review.Edit, "", expectedRevision: review.Revision).Result);
                var pending = store.LoadVault().Pending!;
                vaultProposalId = pending.Id;
                Assert.Equal(identity, pending.Edit.ServiceProposal!.ProposalId);
                Assert.Empty(store.LoadVault().ServiceApprovals);
                Assert.Empty(store.LoadVault().Sites);
            }
            // A fresh Vault never receives a registry dependency. A broken catalog cannot
            // change or prevent confirmation of the already reviewed frozen consequences.
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var unavailable = await new JsonServiceRegistryLoader().LoadDirectoryAsync(Path.Combine(folder.FullName, "missing-registry"));
                Assert.False(unavailable.IsSuccess);
                var vault = new VaultService(store, clock);
                Assert.True(vault.TryGetActivePolicy(out _));
                Assert.Equal(VaultResult.TooEarly, vault.Confirm(vaultProposalId, "").Result);
                clock.Advance(5);
                Assert.Equal(VaultResult.Applied, vault.Confirm(vaultProposalId, "").Result);
            }
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var state = store.LoadVault();
                var approval = Assert.Single(state.ServiceApprovals);
                Assert.Equal(identity, approval.Proposal.ProposalId);
                Assert.Equal(vaultProposalId, approval.VaultProposalId);
                Assert.Single(state.Sites);
                Assert.All(state.Sites, s => Assert.Equal(identity, s.ServiceOriginId));
                Assert.All(approval.Proposal.ProposedDomains, d => Assert.Equal(ProposalAccessState.NewAccess, d.AccessState));
                var entry = Assert.Single(approval.Proposal.ProposedDomains);
                Assert.Null(Assert.Single(entry.Relationships).InfrastructureId);
                Assert.NotNull(entry.Relationships[0].Requirement.Provenance!.Review);
                foreach (var host in new[] { "accounts.google.com", "google.com", "www.mail.google.com", "oauth2.googleapis.com", "drive.google.com", "youtube.com", "new.google.com" })
                    Assert.Equal(AccessClass.Greylist, state.ToPolicy().Classify(new SitePolicyEntry(host, AccessClass.Whitelist).Identity));
            }
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ActualAtomicReplacementFailureKeepsPolicyAndProvenanceTogether()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-ServiceAtomic-");
        try
        {
            var clock = new Clock();
            using var store = new ProtectedAccessStore(folder.FullName);
            store.InitializeWithoutPassword(clock.GetUtcNow());
            store.SaveVault(store.LoadVault() with { Sites = [] });
            var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
            var proposal = new ServiceAccessProposalBuilder().Build(new("arxiv", registry.Revision), registry, store.LoadVault().ToPolicy());
            var vault = new VaultService(store, clock);
            Assert.Equal(VaultResult.Staged, vault.Stage(ServiceVaultProposal.CreateEdit(proposal), "").Result);
            var id = store.LoadVault().Pending!.Id;
            clock.Advance(5);
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            using (var denyReplace = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(VaultResult.Unavailable, vault.Confirm(id, "").Result);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(store.LoadVault().Sites);
            Assert.Empty(store.LoadVault().ServiceApprovals);
            Assert.Equal(id, store.LoadVault().Pending!.Id);
            Assert.Equal(VaultResult.Applied, vault.Confirm(id, "").Result);
            Assert.Single(store.LoadVault().Sites);
            Assert.Single(store.LoadVault().ServiceApprovals);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionFiveMigrationPreservesPasswordRulesAndPendingWaitWithoutInventingApproval(bool passwordRequired)
    {
        const string password = "existing secure password";
        var folder = Directory.CreateTempSubdirectory("Zenith-ServiceMigration-");
        var clock = new Clock();
        try
        {
            Guid id;
            DateTimeOffset eligible;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                if (passwordRequired) store.Initialize(password, clock.GetUtcNow()); else store.InitializeWithoutPassword(clock.GetUtcNow());
                var vault = new VaultService(store, clock);
                Assert.Equal(VaultResult.Staged, vault.Stage(new(AddHost: "new.example"), password).Result);
                var pending = store.LoadVault().Pending!;
                id = pending.Id; eligible = pending.EligibleAt;
            }
            Rewrite(folder.FullName, root =>
            {
                root["Version"] = 5;
                root["Vault"]!.AsObject().Remove("ServiceApprovals");
                root["Vault"]!["Pending"]!["Edit"]!.AsObject().Remove("ServiceProposal");
                foreach (var site in root["Vault"]!["Sites"]!.AsArray()) site!.AsObject().Remove("ServiceOriginId");
            });
            using var reopened = new ProtectedAccessStore(folder.FullName);
            var state = reopened.LoadVault();
            Assert.Equal(passwordRequired, state.PasswordRequired);
            Assert.Equal(passwordRequired, reopened.Verify(password));
            Assert.Equal(id, state.Pending!.Id);
            Assert.Equal(eligible, state.Pending.EligibleAt);
            Assert.Empty(state.ServiceApprovals);
            Assert.All(state.Sites, site => Assert.Null(site.ServiceOriginId));
            Assert.Equal(VaultResult.TooEarly, new VaultService(reopened, clock).Confirm(id, password).Result);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("missing_approvals")]
    [InlineData("null_approvals")]
    [InlineData("missing_origin")]
    [InlineData("missing_proposal")]
    [InlineData("changed_reason")]
    [InlineData("unreviewed")]
    public async Task InvalidCurrentSchemaCannotLoseOrChangeFrozenAttribution(string damage)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-ServiceInvalid-");
        try
        {
            var clock = new Clock();
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.InitializeWithoutPassword(clock.GetUtcNow());
                var catalog = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
                var p = new ServiceAccessProposalBuilder().Build(new("github", catalog.Revision), catalog, store.LoadVault().ToPolicy());
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(ServiceVaultProposal.CreateEdit(p), "").Result);
            }
            Rewrite(folder.FullName, root =>
            {
                var vault = root["Vault"]!;
                var edit = vault["Pending"]!["Edit"]!;
                switch (damage)
                {
                    case "missing_approvals": vault.AsObject().Remove("ServiceApprovals"); break;
                    case "null_approvals": vault["ServiceApprovals"] = null; break;
                    case "missing_origin": vault["Sites"]![0]!.AsObject().Remove("ServiceOriginId"); break;
                    case "missing_proposal": edit.AsObject().Remove("ServiceProposal"); break;
                    case "changed_reason": edit["ServiceProposal"]!["ProposedDomains"]![0]!["Reasons"]![0] = "Changed after review"; break;
                    case "unreviewed": edit["ServiceProposal"]!["ProposedDomains"]![0]!["Relationships"]![0]!["Requirement"]!["Provenance"] = null; break;
                }
            });
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Unavailable, reopened.ConfigurationState);
            Assert.False(new VaultService(reopened, clock).TryGetActivePolicy(out _));
        }
        finally { folder.Delete(true); }
    }

    internal static void Rewrite(string directory, Action<JsonNode> change)
    {
        var path = Path.Combine(directory, "access.bin");
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try
        {
            var root = JsonNode.Parse(bytes)!;
            change(root);
            File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(root), null, DataProtectionScope.CurrentUser));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal sealed class Clock : TimeProvider
    {
        private long _seconds;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => _seconds;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero).AddSeconds(_seconds);
        public void Advance(int seconds) => _seconds += seconds;
    }
}
