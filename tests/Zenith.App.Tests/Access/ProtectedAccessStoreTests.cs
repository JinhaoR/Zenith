using System.Text;
using Zenith.App.Access;
using Zenith.Core.Access;

namespace Zenith.App.Tests.Access;

public sealed class ProtectedAccessStoreTests
{
    private const string Password = "a deliberately long test password";

    [Fact]
    public void PasswordAndCooldownRoundTripWithoutPlaintextOrCredentialReplacement()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-AccessTests-");
        var now = DateTimeOffset.UtcNow;
        try
        {
            using (var store = new ProtectedAccessStore(folder.FullName))
            {
                Assert.Equal(AccessConfigurationState.NeedsSetup, store.ConfigurationState);
                store.Initialize(Password, now);
                Assert.True(store.Verify(Password));
                Assert.False(store.Verify("incorrect"));
                Assert.Throws<InvalidOperationException>(() => store.Initialize("a replacement password", now));
                store.Save(new(now, DateTimeOffset.MinValue,
                    [new("https://outside.example/", now, now.AddMinutes(30))]));
            }
            using var reopened = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Ready, reopened.ConfigurationState);
            Assert.True(reopened.Verify(Password));
            Assert.Single(reopened.Load().Requests);
            var file = File.ReadAllBytes(Path.Combine(folder.FullName, "access.bin"));
            Assert.DoesNotContain(Password, Encoding.UTF8.GetString(file));
            Assert.DoesNotContain("outside.example", Encoding.UTF8.GetString(file));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCorruptStateAfterSetupCannotBecomeFreshSetup(bool corrupt)
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-AccessTests-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.Initialize(Password, DateTimeOffset.UtcNow);
            var path = Path.Combine(folder.FullName, "access.bin");
            if (corrupt) { File.WriteAllText(path, "damaged test data"); }
            else { File.Delete(path); }
            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.Throws<InvalidOperationException>(() => store.Initialize(Password, DateTimeOffset.UtcNow));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void RemovingAllInitializedFilesCannotResetCredentialsInTheRunningSession()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-AccessTests-");
        try
        {
            using var store = new ProtectedAccessStore(folder.FullName);
            store.Initialize(Password, DateTimeOffset.UtcNow);
            File.Delete(Path.Combine(folder.FullName, "access.bin"));
            File.Delete(Path.Combine(folder.FullName, "initialized.bin"));
            Assert.Equal(AccessConfigurationState.Unavailable, store.ConfigurationState);
            Assert.Throws<InvalidOperationException>(() => store.Initialize(Password, DateTimeOffset.UtcNow));
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void SecondInstanceCannotWriteTheSameSecurityState()
    {
        var folder = Directory.CreateTempSubdirectory("Zenith-AccessTests-");
        try
        {
            using var first = new ProtectedAccessStore(folder.FullName);
            using var second = new ProtectedAccessStore(folder.FullName);
            Assert.Equal(AccessConfigurationState.Unavailable, second.ConfigurationState);
            Assert.Throws<InvalidOperationException>(() => second.Initialize(Password, DateTimeOffset.UtcNow));
        }
        finally { folder.Delete(recursive: true); }
    }
}
