using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Core.Filtering;

namespace Zenith.App.Filtering;

internal sealed class BlacklistUpdater : IBlacklistSource, IDisposable
{
    internal const string SourceUrl = "https://raw.githubusercontent.com/StevenBlack/hosts/master/alternates/fakenews-gambling-porn/hosts";
    private const int MaximumBytes = 32 * 1024 * 1024;
    private readonly string _path;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _gate = new(1);
    private Snapshot? _snapshot;
    private string? _error;
    private sealed record Snapshot(HostsBlacklist List, DateTimeOffset UpdatedAt, string Hash);
    private sealed record Cache(string Source, DateTimeOffset UpdatedAt, string Text);

    internal BlacklistUpdater(string directory, HttpClient http, TimeProvider? clock = null)
    {
        _path = Path.Combine(directory, "mandatory-hosts.bin");
        _http = http;
        _clock = clock ?? TimeProvider.System;
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > MaximumBytes + 1024 * 1024) throw new IOException();
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            var cache = JsonSerializer.Deserialize<Cache>(bytes) ?? throw new IOException();
            if (cache.Source != SourceUrl || cache.UpdatedAt > _clock.GetUtcNow() || cache.UpdatedAt < DateTimeOffset.UnixEpoch) throw new IOException();
            _snapshot = CreateSnapshot(cache.Text, cache.UpdatedAt);
        }
        catch (Exception) { _error = "Saved Blacklist unavailable. Browsing waits for a valid update."; }
    }

    public HostsBlacklist? Current => Volatile.Read(ref _snapshot)?.List;
    internal string Status
    {
        get
        {
            var snapshot = Volatile.Read(ref _snapshot);
            return snapshot is null ? _error ?? "Waiting for the first verified Blacklist download. Browsing is unavailable." :
                $"{snapshot.List.Count:N0} blocked hosts · Updated {snapshot.UpdatedAt.ToLocalTime():g}\nSHA-256: {snapshot.Hash}\n{_error ?? "Daily synchronization is active."}";
        }
    }
    internal event Action? Changed;

    internal async Task RunAsync()
    {
        try
        {
            do
            {
                await UpdateAsync(_stop.Token);
                await Task.Delay(TimeSpan.FromHours(1), _clock, _stop.Token);
            } while (!_stop.IsCancellationRequested);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    internal async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var previous = Volatile.Read(ref _snapshot);
            var now = _clock.GetUtcNow();
            if (previous is not null && now >= previous.UpdatedAt && now - previous.UpdatedAt < TimeSpan.FromDays(1)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var downloadToken = timeout.Token;
            using var response = await _http.GetAsync(SourceUrl, HttpCompletionOption.ResponseHeadersRead, downloadToken);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.AbsoluteUri != SourceUrl || response.Content.Headers.ContentLength > MaximumBytes)
                throw new IOException("Unexpected update response.");
            await using var input = await response.Content.ReadAsStreamAsync(downloadToken);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, downloadToken)) > 0)
            {
                if (output.Length + read > MaximumBytes) throw new IOException("Update too large.");
                output.Write(buffer, 0, read);
            }
            var text = new UTF8Encoding(false, true).GetString(output.ToArray());
            var next = CreateSnapshot(text, now);
            if (previous is not null && next.List.Count < previous.List.Count / 2) throw new IOException("Unexpected list shrinkage.");
            Save(new(SourceUrl, now, text));
            Volatile.Write(ref _snapshot, next);
            _error = null;
            if (previous is null || !previous.List.HasSameEntries(next.List)) Changed?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            _error = Current is null ? "Blacklist update failed. Browsing remains unavailable; retrying in one hour." :
                "Update failed validation or download. The last valid Blacklist remains active; retrying in one hour.";
        }
        finally { _gate.Release(); }
    }

    private static Snapshot CreateSnapshot(string text, DateTimeOffset time)
    {
        if (!text.Contains("# Title: StevenBlack/hosts", StringComparison.Ordinal)) throw new IOException("Unexpected source format.");
        var list = HostsBlacklist.Parse(text);
        if (list.Count < 10000) throw new IOException("Incomplete list.");
        return new(list, time, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));
    }

    private void Save(Cache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(cache), null, DataProtectionScope.CurrentUser);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".previous");
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose() => _stop.Cancel();
}
