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

public sealed class PermissionIdentityPersistenceTests
{
    private const string Password = "existing protected test password";

    [Fact]
    public async Task ManualAndServiceAttributionRoundtripWithExactRequirementAndStableIdentity()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionRoundtrip-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        VaultState original;
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.InitializeWithoutPassword(clock.GetUtcNow());
                store.SaveVault(store.LoadVault() with { Sites = [] });
                var vault = new VaultService(store, clock);
                Apply(vault, clock, new(AddHost: "mail.google.com"));
                var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
                var proposal = new ServiceAccessProposalBuilder().Build(new("gmail", registry.Revision), registry, store.LoadVault().ToPolicy());
                Apply(vault, clock, ServiceVaultProposal.CreateEdit(proposal));
                original = store.LoadVault();
                Assert.Equal(8, ReadEnvelope(folder.FullName)["Version"]!.GetValue<int>());
            }
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var actual = store.LoadVault();
                Assert.Equal(original.Sites, actual.Sites);
                Assert.Equal(original.PermissionInstances, actual.PermissionInstances);
                Assert.Equal(original.PermissionAttributions, actual.PermissionAttributions);
                var id = actual.Sites.Single(s => s.Host == "mail.google.com").PermissionInstanceId;
                var attrs = actual.PermissionAttributions.Where(a => a.PermissionInstanceId == id).ToArray();
                Assert.Equal(2, attrs.Length);
                Assert.Contains(attrs, a => a.Source == PermissionAttributionSource.ManualVaultChange);
                var reference = Assert.Single(attrs, a => a.Source == PermissionAttributionSource.ServiceApproval);
                var approval = actual.ServiceApprovals.Single(a => a.VaultProposalId == reference.ServiceApprovalId);
                var requirement = approval.Proposal.ProposedDomains.Single(d => d.Hostname == reference.Requirement!.Hostname)
                    .Relationships[reference.Requirement!.RelationshipIndex];
                Assert.Null(requirement.InfrastructureId);
                Assert.NotNull(requirement.Requirement.Provenance!.Review);
                Apply(new(store, clock), clock, new(RemoveHost: "mail.google.com"));
                Apply(new(store, clock), clock, new(AddHost: "mail.google.com"));
                var recreated = store.LoadVault();
                var newId = recreated.Sites.Single(s => s.Host == "mail.google.com").PermissionInstanceId;
                Assert.NotEqual(id, newId);
                Assert.Equal(PermissionAttributionSource.ManualVaultChange,
                    Assert.Single(recreated.PermissionAttributions, a => a.PermissionInstanceId == newId).Source);
                Assert.Contains(recreated.PermissionInstances, p => p.Id == id);
            }
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task VersionSixMigrationPreservesPolicyAuthenticationCooldownAndFrozenPendingProposal(bool password, bool servicePending)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionMigration-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        VaultState original;
        AccessStateSnapshot access;
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                if (password) store.Initialize(Password, clock.GetUtcNow()); else store.InitializeWithoutPassword(clock.GetUtcNow());
                var vault = new VaultService(store, clock);
                var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
                var approved = new ServiceAccessProposalBuilder().Build(new("github", registry.Revision), registry, store.LoadVault().ToPolicy());
                Apply(vault, clock, ServiceVaultProposal.CreateEdit(approved), password ? Password : "");
                Assert.Equal(AccessSubmissionResult.Accepted,
                    new GreylistAccessService(vault, store, store, clock).SubmitRequest("https://outside.example/", Password).Result);
                var edit = servicePending ? ServiceVaultProposal.CreateEdit(new ServiceAccessProposalBuilder()
                    .Build(new("gmail", registry.Revision), registry, store.LoadVault().ToPolicy())) : new VaultEdit(AddHost: "new.example");
                Assert.Equal(VaultResult.Staged, vault.Stage(edit, Password).Result);
                original = store.LoadVault();
                access = store.Load();
            }
            DowngradeToVersionSix(folder.FullName);
            var legacy = ReadEnvelope(folder.FullName);
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var migrated = store.LoadVault();
                Assert.Equal(original.Revision, migrated.Revision);
                Assert.Equal(original.Settings, migrated.Settings);
                Assert.Equal(original.ToPolicy().Entries, migrated.ToPolicy().Entries);
                Assert.Equal(JsonSerializer.Serialize(original.Pending), JsonSerializer.Serialize(migrated.Pending));
                Assert.Equal(JsonSerializer.Serialize(original.ServiceApprovals), JsonSerializer.Serialize(migrated.ServiceApprovals));
                Assert.Equal(password, migrated.PasswordRequired);
                Assert.Equal(password, store.Verify(Password));
                Assert.Equal(access.Requests, store.Load().Requests);
                Assert.Equal(access.LastObservedUtc, store.Load().LastObservedUtc);
                Assert.Equal(access.RetryAfter, store.Load().RetryAfter);
                Assert.All(migrated.PermissionAttributions, a =>
                {
                    Assert.Equal(PermissionAttributionSource.LegacyMigration, a.Source);
                    Assert.Null(a.ServiceApprovalId);
                    Assert.Null(a.OriginatingVaultOperationId);
                    Assert.Null(a.Requirement);
                });
                Assert.Equal(migrated.Sites.Length, migrated.PermissionInstances.Count);
                var persisted = ReadEnvelope(folder.FullName);
                Assert.Equal(legacy["Salt"]!.GetValue<string>(), persisted["Salt"]!.GetValue<string>());
                Assert.Equal(legacy["Verifier"]!.GetValue<string>(), persisted["Verifier"]!.GetValue<string>());
                Assert.Equal(VaultResult.TooEarly, new VaultService(store, clock).Confirm(migrated.Pending!.Id, Password).Result);
                original = migrated;
            }
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Assert.Equal(original.Sites, store.LoadVault().Sites);
                Assert.Equal(original.PermissionAttributions, store.LoadVault().PermissionAttributions);
                clock.Advance(5);
                Assert.Equal(VaultResult.Applied, new VaultService(store, clock).Confirm(original.Pending!.Id, Password).Result);
            }
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void FailedAtomicMigrationLeavesOriginalBytesAndPendingProposalIntact()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionMigrationFailure-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        try
        {
            Guid pending;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.InitializeWithoutPassword(clock.GetUtcNow());
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(new(AddHost: "new.example"), "").Result);
                pending = store.LoadVault().Pending!.Id;
            }
            DowngradeToVersionSix(folder.FullName);
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            using (var denyReplacement = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
                Assert.ThrowsAny<IOException>(() => store.LoadVault());
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.False(new VaultService(store, clock).TryGetActivePolicy(out _));
            }
            using var retry = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(pending, retry.LoadVault().Pending!.Id);
            Assert.Equal(8, ReadEnvelope(folder.FullName)["Version"]!.GetValue<int>());
            Assert.Empty(Directory.GetFiles(folder.FullName, "*.tmp"));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void InvalidLegacyPolicyCannotBeRepairedOrPartiallyMigratedByAssigningIdentities()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionInvalidLegacy-");
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName)) store.InitializeWithoutPassword(DateTimeOffset.UtcNow);
            DowngradeToVersionSix(folder.FullName);
            ServiceApprovalPersistenceTests.Rewrite(folder.FullName, root =>
                root["Vault"]!["Sites"]!.AsArray().Add(root["Vault"]!["Sites"]![0]!.DeepClone()));
            var before = File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin"));
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Unavailable, reopened.ConfigurationState);
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin")));
            Assert.Equal(6, ReadEnvelope(folder.FullName)["Version"]!.GetValue<int>());
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task ActualFailedConfirmationDoesNotPublishIdentityAttributionOrApproval()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionAtomic-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.InitializeWithoutPassword(clock.GetUtcNow());
            var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
            var vault = new VaultService(store, clock);
            var proposal = new ServiceAccessProposalBuilder().Build(new("gmail", registry.Revision), registry, store.LoadVault().ToPolicy());
            Assert.Equal(VaultResult.Staged, vault.Stage(ServiceVaultProposal.CreateEdit(proposal), "").Result);
            var original = store.LoadVault();
            clock.Advance(5);
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            using (var denyReplace = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(VaultResult.Unavailable, vault.Confirm(original.Pending!.Id, "").Result);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(original.Sites, store.LoadVault().Sites);
            Assert.Equal(original.PermissionInstances, store.LoadVault().PermissionInstances);
            Assert.Equal(original.PermissionAttributions, store.LoadVault().PermissionAttributions);
            Assert.Empty(store.LoadVault().ServiceApprovals);
            Assert.Equal(VaultResult.Applied, vault.Confirm(original.Pending!.Id, "").Result);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("missing_id")]
    [InlineData("empty_id")]
    [InlineData("duplicate_id")]
    [InlineData("missing_instances")]
    [InlineData("missing_attributions")]
    [InlineData("null_attributions")]
    [InlineData("missing_source")]
    [InlineData("wrong_scope")]
    public void CurrentSchemaMetadataCannotAcquireLegacyDefaults(string damage)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionInvalid-");
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName)) store.InitializeWithoutPassword(DateTimeOffset.UtcNow);
            ServiceApprovalPersistenceTests.Rewrite(folder.FullName, root =>
            {
                var vault = root["Vault"]!;
                switch (damage)
                {
                    case "missing_id": vault["Sites"]![0]!.AsObject().Remove("PermissionInstanceId"); break;
                    case "empty_id": vault["Sites"]![0]!["PermissionInstanceId"] = Guid.Empty; break;
                    case "duplicate_id": vault["Sites"]![1]!["PermissionInstanceId"] = vault["Sites"]![0]!["PermissionInstanceId"]!.DeepClone(); break;
                    case "missing_instances": vault.AsObject().Remove("PermissionInstances"); break;
                    case "missing_attributions": vault.AsObject().Remove("PermissionAttributions"); break;
                    case "null_attributions": vault["PermissionAttributions"] = null; break;
                    case "missing_source": vault["PermissionAttributions"]![0]!.AsObject().Remove("Source"); break;
                    case "wrong_scope": vault["PermissionInstances"]![0]!["Host"] = "other.example"; break;
                }
            });
            var before = File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin"));
            using var store2 = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Unavailable, store2.ConfigurationState);
            Assert.False(new VaultService(store2).TryGetActivePolicy(out _));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin")));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public async Task MigrationThatWouldExceedEnvelopeLimitLeavesVersionSixUntouched()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionMigrationSize-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName)) store.InitializeWithoutPassword(clock.GetUtcNow());
            var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
            var basic = new ServiceAccessProposalBuilder().Build(new("arxiv", registry.Revision), registry, new([]));
            var large = new AccessProposal(basic.SelectedService, basic.ProposedDomains, basic.RegistryFingerprint,
                basic.PolicyRevision, basic.ServiceName, basic.OptionalDomains, basic.Conflicts,
                Enumerable.Repeat(new string('w', 1900), 340).ToArray());
            var legacy = new VaultState(1, new(), Enumerable.Range(0, 1000)
                .Select(i => new VaultSite($"host{i}.example", AccessClass.Whitelist, false)).ToArray(),
                clock.GetUtcNow(), DateTimeOffset.MinValue)
            { ServiceApprovals = [new(large, Guid.NewGuid(), clock.GetUtcNow(), 1)] };
            ServiceApprovalPersistenceTests.Rewrite(folder.FullName, root => root["Vault"] = JsonSerializer.SerializeToNode(legacy));
            DowngradeToVersionSix(folder.FullName);
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            Assert.True(before.Length < 1024 * 1024);
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Throws<InvalidDataException>(() => reopened.LoadVault());
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Equal(6, ReadEnvelope(folder.FullName)["Version"]!.GetValue<int>());
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void OversizedHistoryIsRejectedBeforeProtectedStateReplacement()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-PermissionSize-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.InitializeWithoutPassword(DateTimeOffset.UtcNow);
            var original = store.LoadVault();
            var instances = Enumerable.Range(0, 5000).Select(i => new PermissionInstance(Guid.NewGuid(), $"host{i}.example", AccessClass.Whitelist, false, original.Revision)).ToArray();
            var oversized = original with
            {
                PermissionInstances = original.PermissionInstances.Concat(instances).ToArray(),
                PermissionAttributions = original.PermissionAttributions.Concat(instances.Select(p =>
                    new PermissionAttribution(p.Id, PermissionAttributionSource.LegacyMigration, original.Revision))).ToArray()
            };
            oversized.Validate();
            var before = File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin"));
            Assert.Throws<InvalidDataException>(() => store.SaveVault(oversized));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin")));
            Assert.Equal(original.PermissionInstances, store.LoadVault().PermissionInstances);
        }
        finally { folder.Delete(true); }
    }

    private static void Apply(VaultService vault, ServiceApprovalPersistenceTests.Clock clock, VaultEdit edit, string password = "")
    {
        Assert.Equal(VaultResult.Staged, vault.Stage(edit, password).Result);
        clock.Advance(5);
        Assert.Equal(VaultResult.Applied, vault.Confirm(vault.GetStatus().State!.Pending!.Id, password).Result);
    }

    private static void DowngradeToVersionSix(string directory) => ServiceApprovalPersistenceTests.Rewrite(directory, root =>
    {
        root["Version"] = 6;
        var vault = root["Vault"]!.AsObject();
        vault.Remove("PermissionInstances");
        vault.Remove("PermissionAttributions");
        foreach (var site in vault["Sites"]!.AsArray()) site!.AsObject().Remove("PermissionInstanceId");
    });

    private static JsonNode ReadEnvelope(string directory)
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(directory, "access.bin")), null, DataProtectionScope.CurrentUser);
        try { return JsonNode.Parse(bytes)!; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
