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
    public void VersionOneMigratesOncePreservingPasswordAndOldWaits()
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
                Assert.True(store.Verify(Password));
                Assert.Equal(new VaultSettings(5, 5, 5), store.LoadVault().Settings);
                var pending = Assert.Single(store.Load().Requests);
                Assert.Equal(now.AddMinutes(30), pending.EligibleAt);
                Assert.Equal(1800, pending.CooldownSeconds);
                Assert.Equal(3600, pending.GrantSeconds);
                store.SaveVault(store.LoadVault() with { Settings = new(60, 120, 300) });
            }
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(new VaultSettings(60, 120, 300), reopened.LoadVault().Settings);
            Assert.True(reopened.Verify(Password));
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

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; private set; } = DateTimeOffset.UtcNow;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => _ticks;
        public void Advance(int seconds) { Now = Now.AddSeconds(seconds); _ticks += TimeSpan.FromSeconds(seconds).Ticks; }
    }
}
