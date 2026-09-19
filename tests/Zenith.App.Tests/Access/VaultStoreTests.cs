using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zenith.App.Access;
using Zenith.Core.Access;
using Zenith.Core.Vault;

namespace Zenith.App.Tests.Access;

public sealed class VaultStoreTests
{
    private const string Password = "existing vault test password";
    private const string NewPassword = "replacement vault test password";

    [Fact]
    public void VersionFourTurnsOffPromptsWithoutResettingPolicyOrSavedWaits()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultV4-");
        var clock = new Clock();
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.Initialize(Password, clock.Now);
                store.SaveVault(store.LoadVault() with { Settings = new(60, 120, 300), Revision = 7 });
                var vault = new VaultService(store, clock);
                var access = new GreylistAccessService(vault, store, store, clock);
                Assert.Equal(AccessPhase.Cooldown, access.SubmitRequest("https://outside.example/", Password).Status.Phase);
                Assert.Equal(VaultResult.Staged, vault.Stage(new(AddHost: "pending.example"), Password).Result);
                var original = store.LoadVault();
                var requests = store.Load().Requests;
                var path = Path.Combine(folder.FullName, "access.bin");
                var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
                try
                {
                    var data = JsonNode.Parse(plaintext)!;
                    data["Version"] = 4;
                    data["Vault"]!.AsObject().Remove("PasswordRequired");
                    data["Vault"]!["Pending"]!["Edit"]!.AsObject().Remove("DisablePassword");
                    File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                Assert.False(store.PasswordRequired);
                Assert.False(store.Verify(Password));
                Assert.Equal(original.Settings, store.LoadVault().Settings);
                Assert.Equal(original.Revision, store.LoadVault().Revision);
                Assert.Equal(original.Sites, store.LoadVault().Sites);
                Assert.Equal(original.Pending, store.LoadVault().Pending);
                Assert.Equal(requests, store.Load().Requests);
            }
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.False(reopened.PasswordRequired);
            Assert.Equal(7, reopened.LoadVault().Revision);
            Assert.Single(reopened.Load().Requests);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void FreshCooldownOnlyStorageCanOptIntoPasswordAndCannotDefaultMissingMode()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-OptionalPassword-");
        var clock = new Clock();
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.InitializeWithoutPassword(clock.Now);
            Assert.Equal(AccessConfigurationState.Ready, store.ConfigurationState);
            Assert.False(store.PasswordRequired);
            var vault = new VaultService(store, clock);
            Assert.Equal(VaultResult.Staged, vault.Stage(new(ChangePassword: true), "", NewPassword).Result);
            var id = store.LoadVault().Pending!.Id;
            clock.Advance(5);
            Assert.Equal(VaultResult.Applied, vault.Confirm(id, "").Result);
            Assert.True(store.PasswordRequired);
            Assert.True(store.Verify(NewPassword));
            var path = Path.Combine(folder.FullName, "access.bin");
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonNode.Parse(plaintext)!;
                data["Vault"]!.AsObject().Remove("PasswordRequired");
                File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.False(vault.TryGetActivePolicy(out _));
            Assert.Throws<InvalidOperationException>(() => store.InitializeWithoutPassword(clock.Now));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void VersionOneMigratesOnceToCooldownOnlyPreservingOldWaits()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultMigration-");
        var now = DateTimeOffset.UtcNow;
        try
        {
            var salt = RandomNumberGenerator.GetBytes(32);
            var verifier = Rfc2898DeriveBytes.Pbkdf2(Password, salt, 600000, HashAlgorithmName.SHA256, 32);
            var legacy = new { Version = 1, Iterations = 600000, Salt = salt, Verifier = verifier,
                State = new { LastObservedUtc = now, RetryAfter = DateTimeOffset.MinValue,
                    Requests = new[] { new { Target = "https://outside.example/", RequestedAt = now, EligibleAt = now.AddMinutes(30) } } } };
            File.WriteAllBytes(Path.Combine(folder.FullName, "initialized.bin"), ProtectedData.Protect(
                Encoding.UTF8.GetBytes("Zenith access initialized v1"), null, DataProtectionScope.CurrentUser));
            File.WriteAllBytes(Path.Combine(folder.FullName, "access.bin"), ProtectedData.Protect(
                JsonSerializer.SerializeToUtf8Bytes(legacy), null, DataProtectionScope.CurrentUser));
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Assert.False(store.PasswordRequired);
                Assert.Equal(new VaultSettings(5, 5, 5), store.LoadVault().Settings);
                var pending = Assert.Single(store.Load().Requests);
                Assert.Equal(now.AddMinutes(30), pending.EligibleAt);
                Assert.Equal(1800, pending.CooldownSeconds);
                Assert.Equal(3600, pending.GrantSeconds);
                store.SaveVault(store.LoadVault() with { Settings = new(60, 120, 300) });
            }
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(new VaultSettings(60, 120, 300), reopened.LoadVault().Settings);
            Assert.False(reopened.PasswordRequired);
            Assert.True(File.Exists(Path.Combine(folder.FullName, "access.bin.previous")));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void StagedVerifierSurvivesRestartAndChangesPasswordOnlyOnConfirmation()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultPassword-");
        var clock = new Clock();
        try
        {
            Guid id;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.Initialize(Password, clock.Now);
                var vault = new VaultService(store, clock);
                Assert.Equal(VaultResult.Staged, vault.Stage(new(ChangePassword: true), Password, NewPassword).Result);
                id = store.LoadVault().Pending!.Id;
                Assert.True(store.Verify(Password));
                Assert.False(store.Verify(NewPassword));
                var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin")), null, DataProtectionScope.CurrentUser);
                Assert.DoesNotContain(NewPassword, Encoding.UTF8.GetString(plaintext));
                Assert.DoesNotContain(Password, Encoding.UTF8.GetString(plaintext));
                CryptographicOperations.ZeroMemory(plaintext);
            }
            clock.Advance(5);
            using var reopened = new ProtectedAccessStore(folder.FullName);
            var restarted = new VaultService(reopened, clock);
            Assert.Equal(VaultResult.Applied, restarted.Confirm(id, Password).Result);
            Assert.True(reopened.Verify(NewPassword));
            Assert.False(reopened.Verify(Password));
            Assert.Null(reopened.LoadVault().Pending);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void ExistingVersionTwoPendingProposalWithoutRemovalFieldRemainsReadable()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultRemovalCompatibility-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            var now = DateTimeOffset.UtcNow;
            store.Initialize(Password, now);
            var vault = new VaultService(store, new FixedClock(now));
            Assert.Equal(VaultResult.Staged,
                vault.Stage(new(AddHost: "legacy-pending.example"), Password).Result);

            var path = Path.Combine(folder.FullName, "access.bin");
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonNode.Parse(plaintext)!;
                data["Version"] = 2;
                data["Vault"]!["Pending"]!["Edit"]!.AsObject().Remove("RemoveHost");
                File.WriteAllBytes(path, ProtectedData.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }

            Assert.Equal(AccessConfigurationState.Ready, store.ConfigurationState);
            Assert.Null(store.LoadVault().Pending!.Edit.RemoveHost);
            Assert.Equal("legacy-pending.example", store.LoadVault().Pending!.Edit.AddHost);
            var migratedPlaintext = ProtectedData.Unprotect(
                File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var migrated = JsonNode.Parse(migratedPlaintext)!;
                Assert.Equal(5, migrated["Version"]!.GetValue<int>());
                Assert.True(migrated["Vault"]!["Pending"]!["Edit"]!.AsObject().ContainsKey("RemoveHost"));
            }
            finally { CryptographicOperations.ZeroMemory(migratedPlaintext); }
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void MissingRemovalFieldInCurrentPendingSchemaFailsClosed()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultRemovalSchema-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            var now = DateTimeOffset.UtcNow;
            store.Initialize(Password, now);
            var vault = new VaultService(store, new FixedClock(now));
            Assert.Equal(VaultResult.Staged,
                vault.Stage(new(RemoveHost: "github.com"), Password).Result);

            var path = Path.Combine(folder.FullName, "access.bin");
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonNode.Parse(plaintext)!;
                data["Vault"]!["Pending"]!["Edit"]!.AsObject().Remove("RemoveHost");
                File.WriteAllBytes(path, ProtectedData.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }

            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.False(new VaultService(store).TryGetActivePolicy(out _));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("Vault")]
    [InlineData("VaultSeconds")]
    [InlineData("AccessClass")]
    public void MissingCurrentSchemaPolicyDoesNotResetToDevelopmentDefaults(string missing)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultCorruption-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.Initialize(Password, DateTimeOffset.UtcNow);
            var path = Path.Combine(folder.FullName, "access.bin");
            var data = JsonNode.Parse(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))!;
            if (missing == "Vault") data.AsObject().Remove("Vault");
            else if (missing == "AccessClass") data["Vault"]!["Sites"]![0]!.AsObject().Remove(missing);
            else data["Vault"]!["Settings"]!.AsObject().Remove(missing);
            File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.False(new VaultService(store).TryGetActivePolicy(out _));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void BatchSurvivesRestartAndAppliesNamesAndScopesTogether()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultBatch-");
        var clock = new Clock();
        try
        {
            Guid id;
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                store.Initialize(Password, clock.Now);
                var edit = new VaultEdit(AddSites:
                    [new("scholar.google.com", false, "Google Scholar"), new("mail.google.com", false, "Gmail")],
                    RemoveSites: ["google.com"]);
                Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(edit, Password).Result);
                id = store.LoadVault().Pending!.Id;
            }
            clock.Advance(5);
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(2, reopened.LoadVault().Pending!.Edit.Additions().Count());
            Assert.Equal(VaultResult.Applied, new VaultService(reopened, clock).Confirm(id, Password).Result);
            var sites = reopened.LoadVault().Sites;
            Assert.DoesNotContain(sites, site => site.Host == "google.com");
            var scholar = Assert.Single(sites, site => site.Host == "scholar.google.com");
            Assert.Equal("Google Scholar", scholar.DisplayName);
            Assert.False(scholar.IncludeSubdomains);
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void VersionThreeProposalPreservesIdentityDeadlineAndScopeOnMigration()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultV3-");
        var clock = new Clock();
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.Initialize(Password, clock.Now);
            Assert.Equal(VaultResult.Staged, new VaultService(store, clock)
                .Stage(new(AddHost: "legacy.example", IncludeSubdomains: true), Password).Result);
            var pending = store.LoadVault().Pending!;
            var path = Path.Combine(folder.FullName, "access.bin");
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonNode.Parse(plaintext)!;
                data["Version"] = 3;
                var edit = data["Vault"]!["Pending"]!["Edit"]!.AsObject();
                edit.Remove("AddSites");
                edit.Remove("RemoveSites");
                File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            Assert.Equal(pending, store.LoadVault().Pending);
            Assert.False(store.PasswordRequired);
            clock.Advance(5);
            Assert.Equal(VaultResult.Applied, new VaultService(store, clock).Confirm(pending.Id, Password).Result);
            Assert.True(Assert.Single(store.LoadVault().Sites, site => site.Host == "legacy.example").IncludeSubdomains);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("AddSites")]
    [InlineData("RemoveSites")]
    [InlineData("IncludeSubdomains")]
    public void MissingBatchScopeFieldsFailClosed(string missing)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-VaultBatchSchema-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            var clock = new Clock();
            store.Initialize(Password, clock.Now);
            Assert.Equal(VaultResult.Staged, new VaultService(store, clock).Stage(
                new(AddSites: [new("new.example")], RemoveSites: []), Password).Result);
            var path = Path.Combine(folder.FullName, "access.bin");
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonNode.Parse(plaintext)!;
                var edit = data["Vault"]!["Pending"]!["Edit"]!.AsObject();
                if (missing == "IncludeSubdomains") edit["AddSites"]![0]!.AsObject().Remove(missing);
                else edit.Remove(missing);
                File.WriteAllBytes(path, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(data), null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.False(new VaultService(store).TryGetActivePolicy(out _));
        }
        finally { folder.Delete(true); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) { Now = Now.AddSeconds(seconds); _ticks += TimeSpan.FromSeconds(seconds).Ticks; }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
