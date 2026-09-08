using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Core.Access;
using Zenith.Core.Vault;

namespace Zenith.App.Access;

public sealed class ProtectedAccessStore : IVaultStore, IDisposable
{
    private const int Iterations = 600_000;
    private static readonly byte[] Marker = Encoding.UTF8.GetBytes("Zenith access initialized v1");
    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly string _markerPath;
    private readonly FileStream? _lease;
    private bool _disposed;
    private bool _observedInitialization;
    public object SyncRoot => _gate;

    public ProtectedAccessStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zenith", "Access");
        _statePath = Path.Combine(directory, "access.bin");
        _markerPath = Path.Combine(directory, "initialized.bin");
        try
        {
            Directory.CreateDirectory(directory);
            _lease = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public AccessConfigurationState ConfigurationState
    {
        get
        {
            lock (_gate)
            {
                if (_disposed || _lease is null)
                {
                    return AccessConfigurationState.Unavailable;
                }
                if (!File.Exists(_statePath) && !File.Exists(_markerPath))
                {
                    return _observedInitialization ? AccessConfigurationState.Unavailable : AccessConfigurationState.NeedsSetup;
                }
                _observedInitialization = true;
                try
                {
                    _ = ReadEnvelope();
                    return AccessConfigurationState.Ready;
                }
                catch (Exception) { return AccessConfigurationState.Unavailable; }
            }
        }
    }

    public void Initialize(string password, DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureLease();
            if (ConfigurationState != AccessConfigurationState.NeedsSetup || password.Length is < 15 or > 128)
            {
                throw new InvalidOperationException("Access cannot be initialized.");
            }
            var salt = RandomNumberGenerator.GetBytes(32);
            var verifier = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                var envelope = new Envelope(4, Iterations, salt, verifier,
                    new(now, DateTimeOffset.MinValue, []), VaultState.CreateDevelopment(now));
                // A failed initial write leaves a marker and requires recovery; it must
                // never silently offer fresh credential setup over missing state.
                using (var marker = new FileStream(_markerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    marker.Write(ProtectedData.Protect(Marker, null, DataProtectionScope.CurrentUser));
                    marker.Flush(flushToDisk: true);
                    _observedInitialization = true;
                }
                WriteEnvelope(envelope, overwrite: false);
            }
            finally { CryptographicOperations.ZeroMemory(verifier); }
        }
    }

    public bool Verify(string password)
    {
        lock (_gate)
        {
            var envelope = ReadEnvelope();
            if (password.Length is < 1 or > 128)
            {
                return false;
            }
            var computed = Rfc2898DeriveBytes.Pbkdf2(password, envelope.Salt,
                envelope.Iterations, HashAlgorithmName.SHA256, 32);
            try { return CryptographicOperations.FixedTimeEquals(computed, envelope.Verifier); }
            finally { CryptographicOperations.ZeroMemory(computed); }
        }
    }

    public AccessStateSnapshot Load()
    {
        lock (_gate) { return ReadEnvelope().State; }
    }

    public void Save(AccessStateSnapshot state)
    {
        lock (_gate)
        {
            var envelope = ReadEnvelope();
            WriteEnvelope(envelope with { State = state }, overwrite: true);
        }
    }

    public VaultState LoadVault()
    {
        lock (_gate) { return ReadEnvelope().Vault!; }
    }

    public string PreparePassword(string password)
    {
        if (password.Length is < 15 or > 128) throw new ArgumentException("Use 15–128 characters.");
        var salt = RandomNumberGenerator.GetBytes(32);
        var verifier = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        try { return Convert.ToBase64String(salt.Concat(verifier).ToArray()); }
        finally { CryptographicOperations.ZeroMemory(verifier); }
    }

    public void SaveVault(VaultState state, string? replacementVerifier = null)
    {
        lock (_gate)
        {
            state.Validate();
            var envelope = ReadEnvelope() with { Vault = state };
            if (replacementVerifier is not null)
            {
                var bytes = Convert.FromBase64String(replacementVerifier);
                try
                {
                    if (bytes.Length != 64) throw new InvalidDataException("Invalid replacement verifier.");
                    envelope = envelope with { Salt = bytes[..32], Verifier = bytes[32..] };
                }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            WriteEnvelope(envelope, overwrite: true);
        }
    }

    private Envelope ReadEnvelope()
    {
        EnsureLease();
        if (new FileInfo(_markerPath).Length > 4096 || new FileInfo(_statePath).Length > 1024 * 1024)
        {
            throw new InvalidDataException("Access data exceeds its size limit.");
        }
        var marker = ProtectedData.Unprotect(File.ReadAllBytes(_markerPath), null, DataProtectionScope.CurrentUser);
        if (!CryptographicOperations.FixedTimeEquals(marker, Marker))
        {
            throw new InvalidDataException("Invalid initialization marker.");
        }
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(_statePath), null, DataProtectionScope.CurrentUser);
        try
        {
            using var document = JsonDocument.Parse(plaintext);
            var storedVersion = document.RootElement.GetProperty("Version").GetInt32();
            if (storedVersion is 2 or 3 or 4)
            {
                // Missing current-schema fields must not acquire constructor defaults,
                // especially a shorter wait or a longer grant lifetime.
                var vault = document.RootElement.GetProperty("Vault");
                foreach (var field in new[] { "Revision", "Settings", "Sites", "LastObservedUtc", "RetryAfter", "Pending" })
                    _ = vault.GetProperty(field);
                foreach (var field in new[] { "GreylistSeconds", "GrantSeconds", "VaultSeconds" })
                    _ = vault.GetProperty("Settings").GetProperty(field);
                foreach (var site in vault.GetProperty("Sites").EnumerateArray())
                    foreach (var field in new[] { "Host", "AccessClass", "IncludeSubdomains" })
                        _ = site.GetProperty(field);
                var pending = vault.GetProperty("Pending");
                if (pending.ValueKind != JsonValueKind.Null)
                {
                    foreach (var field in new[] { "Id", "BaseRevision", "ProposedAt", "EligibleAt", "Edit", "PasswordVerifier" })
                        _ = pending.GetProperty(field);
                    foreach (var field in new[] { "GreylistSeconds", "GrantSeconds", "VaultSeconds", "AddHost", "IncludeSubdomains", "ChangePassword" })
                        _ = pending.GetProperty("Edit").GetProperty(field);
                    if (storedVersion >= 3)
                        _ = pending.GetProperty("Edit").GetProperty("RemoveHost");
                    if (storedVersion == 4)
                    {
                        var edit = pending.GetProperty("Edit");
                        var additions = edit.GetProperty("AddSites");
                        _ = edit.GetProperty("RemoveSites");
                        if (additions.ValueKind != JsonValueKind.Null)
                            foreach (var addition in additions.EnumerateArray())
                            {
                                _ = addition.GetProperty("Host");
                                _ = addition.GetProperty("IncludeSubdomains");
                            }
                    }
                }
                foreach (var field in new[] { "LastObservedUtc", "RetryAfter", "Requests" })
                    _ = document.RootElement.GetProperty("State").GetProperty(field);
                foreach (var request in document.RootElement.GetProperty("State").GetProperty("Requests").EnumerateArray())
                {
                    foreach (var field in new[] { "Target", "RequestedAt", "EligibleAt", "CooldownSeconds", "GrantSeconds" })
                        _ = request.GetProperty(field);
                }
            }
            var envelope = JsonSerializer.Deserialize<Envelope>(plaintext);
            if (envelope is not { Version: 1 or 2 or 3 or 4, Iterations: Iterations, Salt.Length: 32, Verifier.Length: 32, State: not null })
            {
                throw new InvalidDataException("Invalid access envelope.");
            }
            if (envelope.Version == 1)
            {
                envelope = envelope with { Vault = VaultState.CreateDevelopment(envelope.State.LastObservedUtc) };
            }
            if (envelope.Vault is null) throw new InvalidDataException("Missing initialized Vault policy.");
            envelope.Vault.Validate();
            if (envelope.Vault.Pending?.PasswordVerifier is { } pendingVerifier && Convert.FromBase64String(pendingVerifier).Length != 64)
                throw new InvalidDataException("Invalid pending verifier.");
            if (storedVersion < 4)
            {
                envelope = envelope with { Version = 4 };
                WriteEnvelope(envelope, overwrite: true);
            }
            return envelope;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private void WriteEnvelope(Envelope envelope, bool overwrite)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var temporary = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(encrypted);
                file.Flush(flushToDisk: true);
            }
            if (overwrite) File.Replace(temporary, _statePath, $"{_statePath}.previous");
            else File.Move(temporary, _statePath);
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private void EnsureLease()
    {
        if (_disposed || _lease is null)
        {
            throw new InvalidOperationException("Temporary access storage is in use or unavailable.");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _lease?.Dispose();
        }
    }

    private sealed record Envelope(int Version, int Iterations, byte[] Salt, byte[] Verifier, AccessStateSnapshot State,
        VaultState? Vault = null);
}
