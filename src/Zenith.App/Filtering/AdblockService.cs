using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zenith.Core.Filtering;

namespace Zenith.App.Filtering;

/// <summary>Owns replaceable filter snapshots. Publication never races a native engine call.</summary>
internal sealed class AdblockService : IResourceFilterEngine, IDisposable
{
    internal const string EasyListUrl = "https://easylist.to/easylist/easylist.txt";
    internal const string EasyPrivacyUrl = "https://easylist.to/easylist/easyprivacy.txt";
    private const int MaximumBytes = 16 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _updateGate = new(1);
    private readonly CancellationTokenSource _stop = new();
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;
    private readonly string _path;
    private AdblockEngine? _engine;
    private Cache? _active;
    private bool _disposed;
    private string? _error;
    private string _hash = "";
    private string _versions = "";
    private long _blocked;
    private sealed record Cache(DateTimeOffset UpdatedAt, string EasyList, string EasyPrivacy);

    internal AdblockService(string directory, HttpClient http, TimeProvider? clock = null)
    {
        _http = http;
        _clock = clock ?? TimeProvider.System;
        _path = Path.Combine(directory, "resource-filters.bin");
        Cache? cache = null;
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > MaximumBytes * 3) throw new IOException();
                cache = JsonSerializer.Deserialize<Cache>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser));
                if (cache is null || cache.UpdatedAt > _clock.GetUtcNow() || cache.UpdatedAt < DateTimeOffset.UnixEpoch) throw new IOException();
                Validate(cache);
            }
        }
        catch (Exception) { cache = null; _error = "Saved resource filters unavailable; using bundled lists."; }
        if (cache is not null)
        {
            try { ActivateInitial(cache); return; }
            catch (Exception) { _error = "Saved resource filters could not be compiled; using bundled lists."; }
        }
        try { ActivateInitial(new(DateTimeOffset.UnixEpoch, AdblockEngine.ReadAsset("easylist.txt"), AdblockEngine.ReadAsset("easyprivacy.txt"))); }
        catch (Exception) { _error = "Resource filtering unavailable. Subresources are blocked until a valid engine is loaded."; }
    }

    private void ActivateInitial(Cache cache)
    {
        Validate(cache);
        _engine = CreateEngine(cache);
        _active = cache;
        _hash = Hash(cache);
        _versions = Versions(cache);
    }

    internal string Status
    {
        get
        {
            lock (_gate)
            {
                var version = _active is null ? "No active snapshot" : _active.UpdatedAt == DateTimeOffset.UnixEpoch
                    ? "Bundled snapshots" : $"Updated {_active.UpdatedAt.ToLocalTime():g}";
                return $"Ghostery 2.18.2 · EasyList + EasyPrivacy\n{version} · {_engine?.NetworkRuleCount ?? 0:N0} network / {_engine?.CosmeticRuleCount ?? 0:N0} cosmetic rules\n{_versions}\n{Interlocked.Read(ref _blocked):N0} resource requests blocked this session\nSHA-256: {_hash}\n{_error ?? "Daily synchronization active. Reload pages after an update for consistent cosmetic exceptions."}";
            }
        }
    }

    public bool Blocks(ResourceRequest request)
    {
        lock (_gate)
        {
            try
            {
                var blocked = _engine?.Blocks(request) ?? true;
                if (blocked) Interlocked.Increment(ref _blocked);
                return blocked;
            }
            catch (Exception)
            {
                _error = "A resource-filter evaluation failed; that request was blocked.";
                return true;
            }
        }
    }

    internal string Cosmetics(string url, string[] classes, string[] ids, string[] hrefs)
    {
        lock (_gate)
        {
            try { return _engine?.Cosmetics(url, classes, ids, hrefs) ?? ""; }
            catch (Exception) { _error = "Cosmetic filtering failed for a document. Network and site protection remain active."; return ""; }
        }
    }

    internal async Task RunAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await UpdateAsync(_stop.Token);
                await Task.Delay(TimeSpan.FromHours(1), _clock, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    internal async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken);
        AdblockEngine? next = null;
        try
        {
            Cache? previous;
            lock (_gate) { if (_disposed) return; previous = _active; }
            var now = _clock.GetUtcNow();
            if (previous is not null && now >= previous.UpdatedAt && now - previous.UpdatedAt < TimeSpan.FromDays(1)) return;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            var list = await DownloadAsync(EasyListUrl, timeout.Token);
            var privacy = await DownloadAsync(EasyPrivacyUrl, timeout.Token);
            var cache = new Cache(now, list, privacy);
            Validate(cache);
            if (previous is not null && (RuleCount(list) < RuleCount(previous.EasyList) / 2 || RuleCount(privacy) < RuleCount(previous.EasyPrivacy) / 2))
                throw new IOException("Unexpected list shrinkage.");
            next = CreateEngine(cache);
            timeout.Token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_disposed) return;
                if (_engine is not null && (next.NetworkRuleCount < _engine.NetworkRuleCount / 2 || next.CosmeticRuleCount < _engine.CosmeticRuleCount / 2))
                    throw new IOException("Unexpected compiled rule shrinkage.");
                Save(cache);
                var old = _engine;
                _engine = next;
                next = null;
                _active = cache;
                _hash = Hash(cache);
                _versions = Versions(cache);
                _error = null;
                old?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _stop.IsCancellationRequested) { throw; }
        catch (Exception) { lock (_gate) { _error = "Resource-filter update failed. The previous snapshot remains active; retrying in one hour."; } }
        finally { next?.Dispose(); _updateGate.Release(); }
    }

    private async Task<string> DownloadAsync(string url, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != url || response.Content.Headers.ContentLength > MaximumBytes) throw new IOException();
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > MaximumBytes) throw new IOException();
            output.Write(buffer, 0, read);
        }
        return new UTF8Encoding(false, true).GetString(output.ToArray());
    }

    private static int RuleCount(string text) => text.Split('\n').Count(line => line.Length > 0 && line[0] is not ('!' or '['));
    private static string Hash(Cache cache) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cache.EasyList + "\n" + cache.EasyPrivacy)));
    private static string Versions(Cache cache)
    {
        static string Version(string text)
        {
            using var reader = new StringReader(text);
            for (var i = 0; i < 16 && reader.ReadLine() is { } line; i++)
                if (line.StartsWith("! Version: ", StringComparison.Ordinal)) return line[11..];
            return "unreported";
        }
        return $"EasyList {Version(cache.EasyList)} · EasyPrivacy {Version(cache.EasyPrivacy)}";
    }
    private static AdblockEngine CreateEngine(Cache cache)
    {
        var engine = new AdblockEngine(cache.EasyList + "\n" + cache.EasyPrivacy);
        if (engine.NetworkRuleCount >= 10000 && engine.CosmeticRuleCount >= 1000) return engine;
        engine.Dispose();
        throw new IOException("Incomplete compiled filters.");
    }
    private static void Validate(Cache cache)
    {
        ValidateList(cache.EasyList, "EasyList");
        ValidateList(cache.EasyPrivacy, "EasyPrivacy");
    }
    private static void ValidateList(string text, string title)
    {
        if (text.Length > MaximumBytes || !text.StartsWith("[Adblock Plus ", StringComparison.Ordinal)
            || !text.Contains("! Title: " + title + "\n", StringComparison.Ordinal) && !text.Contains("! Title: " + title + "\r\n", StringComparison.Ordinal)
            || text.Contains('\0') || RuleCount(text) < 10000 || text.Split('\n').Any(line => line.Length > 32768))
            throw new IOException("Invalid filter list.");
    }

    private void Save(Cache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(cache), null, DataProtectionScope.CurrentUser);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes); output.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".previous");
            else File.Move(temporary, _path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_gate) { if (_disposed) return; _disposed = true; _engine?.Dispose(); _engine = null; }
    }
}
