using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Core.Access;
using Zenith.Core.Vault;
using Zenith.Core.Registry;

namespace Zenith.App.Access;

public sealed class ProtectedAccessStore : IVaultStore, IDisposable
{
    private const int Iterations = 600_000;
    private static readonly JsonSerializerOptions JsonOptions = new() { RespectRequiredConstructorParameters = true };
    private static readonly byte[] Marker = Encoding.UTF8.GetBytes("Zenith access initialized v1");
    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly string _markerPath;
    private readonly FileStream? _lease;
    private bool _disposed;
    private bool _observedInitialization;
    public object SyncRoot => _gate;
    public bool PasswordRequired { get { lock (_gate) { return ReadEnvelope().Vault!.PasswordRequired; } } }

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

    public void Initialize(string password, DateTimeOffset now) => InitializeState(password, now);

    public void InitializeWithoutPassword(DateTimeOffset now) => InitializeState(null, now);

    private void InitializeState(string? password, DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureLease();
            if (ConfigurationState != AccessConfigurationState.NeedsSetup || (password is not null && password.Length is < 15 or > 128))
            {
                throw new InvalidOperationException("Access cannot be initialized.");
            }
            var salt = RandomNumberGenerator.GetBytes(32);
            var verifier = password is null ? new byte[32] : Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
            try
            {
                var envelope = new Envelope(8, Iterations, salt, verifier,
                    new(now, DateTimeOffset.MinValue, []), VaultState.CreateDevelopment(now) with { PasswordRequired = password is not null });
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
            if (!envelope.Vault!.PasswordRequired || password.Length is < 1 or > 128)
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
            if (!state.PasswordRequired) envelope = envelope with { Verifier = new byte[32] };
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
            if (storedVersion is 2 or 3 or 4 or 5 or 6 or 7 or 8)
            {
                // Missing current-schema fields must not acquire constructor defaults,
                // especially a shorter wait or a longer grant lifetime.
                var vault = document.RootElement.GetProperty("Vault");
                if (storedVersion >= 5) _ = vault.GetProperty("PasswordRequired").GetBoolean();
                if (storedVersion >= 6) ValidateServiceMetadata(vault);
                if (storedVersion >= 7) ValidatePermissionMetadata(vault);
                if (storedVersion >= 8) ValidateRegistryMetadata(vault);
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
                    if (storedVersion >= 4)
                    {
                        var edit = pending.GetProperty("Edit");
                        if (storedVersion >= 5) _ = edit.GetProperty("DisablePassword").GetBoolean();
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
            var envelope = JsonSerializer.Deserialize<Envelope>(plaintext, JsonOptions);
            if (envelope is not { Version: 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8, Iterations: Iterations, Salt.Length: 32, Verifier.Length: 32, State: not null })
            {
                throw new InvalidDataException("Invalid access envelope.");
            }
            if (envelope.Version == 1)
            {
                envelope = envelope with { Vault = VaultState.CreateDevelopment(envelope.State.LastObservedUtc) };
            }
            if (envelope.Vault is null) throw new InvalidDataException("Missing initialized Vault policy.");
            if (storedVersion < 8 && (envelope.Vault.InfrastructureActivations.Count != 0 || envelope.Vault.LocalServiceExtensions.Count != 0 ||
                envelope.Vault.Pending?.Edit.InfrastructureProposal is not null || envelope.Vault.Pending?.Edit.LocalExtensionProposal is not null))
                throw new InvalidDataException("Unexpected registry activation data in a legacy envelope.");
            // Validate and prepare the complete migration before the single replacement.
            // No identity is exposed until that replacement succeeds.
            if (storedVersion < 7)
                envelope = envelope with { Vault = VaultPermissionLedger.MigrateLegacy(envelope.Vault) };
            else envelope.Vault.Validate();
            if (envelope.Vault.Pending?.PasswordVerifier is { } pendingVerifier && Convert.FromBase64String(pendingVerifier).Length != 64)
                throw new InvalidDataException("Invalid pending verifier.");
            if (storedVersion < 5)
            {
                // Explicit product migration: keep policy, proposal IDs and captured
                // waits, but turn off mandatory passwords for existing profiles too.
                envelope = envelope with { Version = 5, Verifier = new byte[32],
                    State = envelope.State with { RetryAfter = DateTimeOffset.MinValue },
                    Vault = envelope.Vault with { PasswordRequired = false, RetryAfter = DateTimeOffset.MinValue } };
            }
            if (storedVersion < 6)
            {
                // No historical service intent is inferred. Version 5 password settings,
                // pending proposals, revisions and timing survive this migration unchanged.
                envelope = envelope with { Vault = envelope.Vault with { ServiceApprovals = Array.Empty<ServiceApproval>() } };
            }
            if (storedVersion < 8)
            {
                // Version 7 identities, frozen approvals and pending consequences stay
                // intact. New collections start empty; no host overlap is reinterpreted.
                envelope = envelope with { Version = 8 };
                envelope.Vault.Validate();
                WriteEnvelope(envelope, overwrite: true);
            }
            return envelope;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private void WriteEnvelope(Envelope envelope, bool overwrite)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        if (encrypted.Length > 1024 * 1024) throw new InvalidDataException("Access data exceeds its size limit; no change was saved.");
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

    private static void ValidateRegistryMetadata(JsonElement vault)
    {
        foreach (var activation in vault.GetProperty("InfrastructureActivations").EnumerateArray())
        {
            foreach (var field in new[] { "Proposal", "VaultOperationId", "ApprovedAt", "AppliedPolicyRevision", "CreatedPermissionIds" })
                _ = activation.GetProperty(field);
            ValidateInfrastructure(activation.GetProperty("Proposal"));
        }
        foreach (var extension in vault.GetProperty("LocalServiceExtensions").EnumerateArray())
        {
            foreach (var field in new[] { "Proposal", "VaultOperationId", "ApprovedAt", "AppliedPolicyRevision", "PermissionInstanceId" })
                _ = extension.GetProperty(field);
            ValidateLocal(extension.GetProperty("Proposal"));
        }
        if (vault.GetProperty("Pending") is { ValueKind: not JsonValueKind.Null } pending)
        {
            var edit = pending.GetProperty("Edit");
            if (edit.GetProperty("InfrastructureProposal") is { ValueKind: not JsonValueKind.Null } baseline) ValidateInfrastructure(baseline);
            if (edit.GetProperty("LocalExtensionProposal") is { ValueKind: not JsonValueKind.Null } local) ValidateLocal(local);
        }
        static void ValidateInfrastructure(JsonElement json)
        {
            var proposal = json.Deserialize<InfrastructureBaselineProposal>(JsonOptions) ?? throw new InvalidDataException("Missing infrastructure proposal.");
            if (json.GetProperty("ProposalId").GetString() != proposal.ProposalId)
                throw new InvalidDataException("Infrastructure proposal identity changed.");
        }
        static void ValidateLocal(JsonElement json)
        {
            var proposal = json.Deserialize<LocalServiceExtensionProposal>(JsonOptions) ?? throw new InvalidDataException("Missing local exception proposal.");
            if (json.GetProperty("ProposalId").GetString() != proposal.ProposalId)
                throw new InvalidDataException("Local exception proposal identity changed.");
        }
    }

    private static void ValidatePermissionMetadata(JsonElement vault)
    {
        foreach (var site in vault.GetProperty("Sites").EnumerateArray())
            _ = site.GetProperty("PermissionInstanceId").GetGuid();
        foreach (var instance in vault.GetProperty("PermissionInstances").EnumerateArray())
            foreach (var field in new[] { "Id", "Host", "AccessClass", "IncludeSubdomains", "CreatedPolicyRevision" })
                _ = instance.GetProperty(field);
        foreach (var attribution in vault.GetProperty("PermissionAttributions").EnumerateArray())
        {
            foreach (var field in new[] { "PermissionInstanceId", "Source", "RecordedPolicyRevision",
                         "OriginatingVaultOperationId", "ServiceApprovalId", "Requirement" })
                _ = attribution.GetProperty(field);
            if (attribution.GetProperty("Requirement") is { ValueKind: not JsonValueKind.Null } requirement)
            {
                _ = requirement.GetProperty("Hostname");
                _ = requirement.GetProperty("RelationshipIndex");
            }
        }
    }

    private static void ValidateServiceMetadata(JsonElement vault)
    {
        foreach (var site in vault.GetProperty("Sites").EnumerateArray()) _ = site.GetProperty("ServiceOriginId");
        foreach (var approval in vault.GetProperty("ServiceApprovals").EnumerateArray())
            ValidateProposal(approval.GetProperty("Proposal"));
        if (vault.GetProperty("Pending") is { ValueKind: not JsonValueKind.Null } pending)
        {
            var proposal = pending.GetProperty("Edit").GetProperty("ServiceProposal");
            if (proposal.ValueKind != JsonValueKind.Null) ValidateProposal(proposal);
        }

        static void ValidateProposal(JsonElement json)
        {
            foreach (var field in new[] { "SelectedService", "ProposedDomains", "RegistryFingerprint", "PolicyRevision", "ServiceName", "OptionalDomains", "Conflicts", "Warnings", "ProposalId" })
                _ = json.GetProperty(field);
            _ = json.GetProperty("SelectedService").GetProperty("OptionalHostnames");
            var proposal = json.Deserialize<AccessProposal>(JsonOptions) ?? throw new InvalidDataException("Missing frozen service proposal.");
            if (!proposal.CanStage || json.GetProperty("ProposalId").GetString() != proposal.ProposalId)
                throw new InvalidDataException("Frozen service proposal identity is invalid.");
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
