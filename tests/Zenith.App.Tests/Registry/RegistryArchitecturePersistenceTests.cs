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

public sealed class RegistryArchitecturePersistenceTests
{
    private const string Password = "existing test password";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrozenBaselineAndLocalExceptionsRoundtripWithoutCatalogAndRemoveThroughVault(bool password)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-RegistryArchitecture-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        try
        {
            var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
            Guid pendingId;
            string proposalId;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Initialize(store, clock, password);
                var proposal = InfrastructureBaselineProposal.Prepare(registry, store.LoadVault().ToPolicy());
                Assert.Equal(new[] { "accounts.google.com" }, proposal.Hostnames);
                proposalId = proposal.ProposalId;
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(RegistryVaultProposal.CreateEdit(proposal), Password).Result);
                pendingId = store.LoadVault().Pending!.Id;
                Assert.Empty(store.LoadVault().Sites);
            }
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var failed = await new JsonServiceRegistryLoader().LoadDirectoryAsync(Path.Combine(folder.FullName, "absent"));
                Assert.False(failed.IsSuccess);
                var vault = new VaultService(store, clock);
                Assert.True(vault.TryGetActivePolicy(out _));
                Assert.Equal(proposalId, store.LoadVault().Pending!.Edit.InfrastructureProposal!.ProposalId);
                Assert.Equal(VaultResult.TooEarly, vault.Confirm(pendingId, Password).Result);
                clock.Advance(5);
                Assert.Equal(VaultResult.Applied, vault.Confirm(pendingId, Password).Result);
                Apply(vault, clock, new(AddHost: "mail.google.com"));
                var local = LocalServiceExtensionProposal.Prepare(registry, "gmail", "login.university.example", "Institution authentication", store.LoadVault().ToPolicy());
                Apply(vault, clock, RegistryVaultProposal.CreateEdit(local, store.LoadVault().ToPolicy()));
            }
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var state = store.LoadVault();
                Assert.Equal(password, state.PasswordRequired);
                Assert.Equal(proposalId, Assert.Single(state.InfrastructureActivations).Proposal.ProposalId);
                var local = Assert.Single(state.GetActiveLocalExtensions());
                Assert.Equal("login.university.example", local.Proposal.Hostname);
                Assert.Empty(registry.FindServicesAssociatedWithHostname(local.Proposal.Hostname));
                Assert.Single(state.GetInfrastructureCreatedHosts());
                Assert.Empty(state.ServiceApprovals);
                Apply(new(store, clock), clock, new(RemoveHost: local.Proposal.Hostname));
                Assert.Empty(store.LoadVault().GetActiveLocalExtensions());
                Assert.Single(store.LoadVault().LocalServiceExtensions);
            }
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionSevenMigrationPreservesLegacyExpandedApprovalPendingProposalIdentityPasswordAndWaits(bool password)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-RegistryV7-");
        var clock = new ServiceApprovalPersistenceTests.Clock();
        try
        {
            VaultState original;
            AccessStateSnapshot access;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Initialize(store, clock, password);
                var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
                var vault = new VaultService(store, clock);
                Apply(vault, clock, ServiceVaultProposal.CreateEdit(LegacyGmail(registry, store.LoadVault().ToPolicy())));
                Assert.Equal(AccessSubmissionResult.Accepted, new GreylistAccessService(vault, store, store, clock)
                    .SubmitRequest("https://outside.example/", Password).Result);
                // A second historical review of existing access remains a valid pending receipt.
                Assert.Equal(VaultResult.Staged, vault.Stage(ServiceVaultProposal.CreateEdit(LegacyGmail(registry, store.LoadVault().ToPolicy())), Password).Result);
                original = store.LoadVault();
                access = store.Load();
            }
            DowngradeToSeven(folder.FullName);
            var legacy = ReadEnvelope(folder.FullName);
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                var state = store.LoadVault();
                Assert.Equal(original.Sites, state.Sites);
                Assert.Equal(original.PermissionInstances, state.PermissionInstances);
                Assert.Equal(original.PermissionAttributions, state.PermissionAttributions);
                Assert.Equal(JsonSerializer.Serialize(original.ServiceApprovals), JsonSerializer.Serialize(state.ServiceApprovals));
                Assert.Equal(JsonSerializer.Serialize(original.Pending), JsonSerializer.Serialize(state.Pending));
                Assert.Equal(access.Requests, store.Load().Requests);
                Assert.Equal(original.Settings, state.Settings);
                Assert.Equal(original.Revision, state.Revision);
                Assert.Equal(password, state.PasswordRequired);
                Assert.Equal(password, store.Verify(Password));
                Assert.Empty(state.InfrastructureActivations);
                Assert.Empty(state.LocalServiceExtensions);
                Assert.Empty(state.GetInfrastructureCreatedHosts()); // no reinterpretation of old Gmail sign-in grants
                var current = ReadEnvelope(folder.FullName);
                Assert.Equal(8, current["Version"]!.GetValue<int>());
                Assert.Equal(legacy["Salt"]!.GetValue<string>(), current["Salt"]!.GetValue<string>());
                Assert.Equal(legacy["Verifier"]!.GetValue<string>(), current["Verifier"]!.GetValue<string>());
                var vault = new VaultService(store, clock);
                Assert.Equal(VaultResult.TooEarly, vault.Confirm(state.Pending!.Id, Password).Result);
                clock.Advance(5);
                Assert.Equal(VaultResult.Applied, vault.Confirm(state.Pending.Id, Password).Result);
                Assert.Equal(2, store.LoadVault().ServiceApprovals.Count);
                Assert.Contains(store.LoadVault().Sites, s => s.Host == "accounts.google.com");
            }
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedVersionSevenMigrationLeavesOriginalBytesIntact(bool corrupt)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-RegistryMigrationFailure-");
        try
        {
            var clock = new ServiceApprovalPersistenceTests.Clock();
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.Initialize(Password, clock.GetUtcNow());
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(new(AddHost: "pending.example"), Password).Result);
            }
            DowngradeToSeven(folder.FullName);
            if (corrupt) ServiceApprovalPersistenceTests.Rewrite(folder.FullName, root => root["Vault"]!["PermissionAttributions"] = new JsonArray());
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            using (var store = new ProtectedAccessStore(folder.FullName))
            using (var denyReplace = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
                Assert.False(new VaultService(store, clock).TryGetActivePolicy(out _));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
            Assert.Equal(7, ReadEnvelope(folder.FullName)["Version"]!.GetValue<int>());
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicConfirmationFailureCannotPublishRegistryMetadataWithoutPermissionOrViceVersa(bool local)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-RegistryAtomic-");
        try
        {
            var clock = new ServiceApprovalPersistenceTests.Clock();
            using var store = new ProtectedAccessStore(folder.FullName);
            Initialize(store, clock, false);
            var vault = new VaultService(store, clock);
            var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
            VaultEdit edit;
            if (local)
            {
                Apply(vault, clock, new(AddHost: "mail.google.com"));
                edit = RegistryVaultProposal.CreateEdit(LocalServiceExtensionProposal.Prepare(registry, "gmail", "login.example", "Sign-in", store.LoadVault().ToPolicy()), store.LoadVault().ToPolicy());
            }
            else edit = RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(registry, store.LoadVault().ToPolicy()));
            Assert.Equal(VaultResult.Staged, vault.Stage(edit, "").Result);
            var id = store.LoadVault().Pending!.Id;
            clock.Advance(5);
            var path = Path.Combine(folder.FullName, "access.bin");
            var before = File.ReadAllBytes(path);
            using (var denyReplace = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(VaultResult.Unavailable, vault.Confirm(id, "").Result);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.Empty(store.LoadVault().InfrastructureActivations);
            Assert.Empty(store.LoadVault().LocalServiceExtensions);
            Assert.Equal(id, store.LoadVault().Pending!.Id);
            Assert.Equal(VaultResult.Applied, vault.Confirm(id, "").Result);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("missing_history")]
    [InlineData("missing_pending")]
    [InlineData("changed_endpoint")]
    [InlineData("changed_identity")]
    public async Task InvalidVersionEightMetadataFailsClosed(string damage)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-RegistryInvalid-");
        try
        {
            var clock = new ServiceApprovalPersistenceTests.Clock();
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Initialize(store, clock, false);
                var registry = (await new JsonServiceRegistryLoader().LoadBundledAsync()).Registry!;
                var edit = RegistryVaultProposal.CreateEdit(InfrastructureBaselineProposal.Prepare(registry, store.LoadVault().ToPolicy()));
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(edit, "").Result);
            }
            ServiceApprovalPersistenceTests.Rewrite(folder.FullName, root =>
            {
                var state = root["Vault"]!.AsObject();
                var edit = state["Pending"]!["Edit"]!.AsObject();
                switch (damage)
                {
                    case "missing_history": state.Remove("LocalServiceExtensions"); break;
                    case "missing_pending": edit.Remove("InfrastructureProposal"); break;
                    case "changed_endpoint": edit["InfrastructureProposal"]!["Endpoints"]![0]!["Requirement"]!["Hostname"] = "evil.example"; break;
                    case "changed_identity": edit["InfrastructureProposal"]!["ProposalId"] = new string('0', 64); break;
                }
            });
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Unavailable, reopened.ConfigurationState);
            Assert.False(new VaultService(reopened, clock).TryGetActivePolicy(out _));
        }
        finally { folder.Delete(true); }
    }

    private static AccessProposal LegacyGmail(ServiceRegistry registry, SitePolicySnapshot policy)
    {
        var domains = registry.GetDomainRequirements("gmail")
            .Select(a => new ProposalRelationship(a.ServiceId, a.InfrastructureId, a.Domain, a.InfrastructureProvenance))
            .Where(ServiceAccessProposalBuilder.IsEligible).OrderBy(a => a.Requirement.Hostname).Select(a =>
            {
                var id = new SitePolicyEntry(a.Requirement.Hostname, AccessClass.Whitelist).Identity;
                var allowed = policy.Classify(id) == AccessClass.Whitelist;
                return new ProposedDomain(a.Requirement.Hostname, [a.Requirement.Purpose], [a],
                    allowed ? ProposalAccessState.AlreadyAllowed : ProposalAccessState.NewAccess,
                    allowed ? [new(a.Requirement.Hostname, false)] : []);
            }).ToArray();
        return new(new("gmail", registry.Revision), domains, RegistryContentIdentity.Registry(registry), policy.Revision, "Gmail");
    }

    private static void Initialize(ProtectedAccessStore store, ServiceApprovalPersistenceTests.Clock clock, bool password)
    {
        if (password) store.Initialize(Password, clock.GetUtcNow()); else store.InitializeWithoutPassword(clock.GetUtcNow());
        store.SaveVault(store.LoadVault() with { Sites = [] });
    }
    private static void Apply(VaultService vault, ServiceApprovalPersistenceTests.Clock clock, VaultEdit edit)
    {
        Assert.Equal(VaultResult.Staged, vault.Stage(edit, Password).Result);
        var id = vault.GetStatus().State!.Pending!.Id;
        clock.Advance(5);
        Assert.Equal(VaultResult.Applied, vault.Confirm(id, Password).Result);
    }
    private static void DowngradeToSeven(string folder) => ServiceApprovalPersistenceTests.Rewrite(folder, root =>
    {
        root["Version"] = 7;
        var state = root["Vault"]!.AsObject();
        state.Remove("InfrastructureActivations"); state.Remove("LocalServiceExtensions");
        if (state["Pending"] is { } pending)
        {
            pending["Edit"]!.AsObject().Remove("InfrastructureProposal");
            pending["Edit"]!.AsObject().Remove("LocalExtensionProposal");
        }
    });
    private static JsonNode ReadEnvelope(string folder)
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder, "access.bin")), null, DataProtectionScope.CurrentUser);
        try { return JsonNode.Parse(bytes)!; }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
