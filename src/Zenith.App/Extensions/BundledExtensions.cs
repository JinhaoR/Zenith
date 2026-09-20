using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Zenith.App.Extensions;

// Application configuration, never populated from web content or a user-selected folder.
internal static class BundledExtensions
{
    internal const string UbolVersion = "2026.907.2003";
    internal const string UbolSha256 = "1FD67CAD123BF677AA23CC7E1EFB524B219EFAABCF586F211843850662B9B1D2";
    internal static string UbolDirectory => Path.Combine(AppContext.BaseDirectory, "BundledExtensions", "uBOLite");

    internal static void VerifyPackage() => VerifyPackage(
        Path.Combine(AppContext.BaseDirectory, "Extensions", "Bundled", "uBOLite.edge.zip"),
        UbolDirectory, UbolSha256);

    // Check before creating the environment: a persisted extension may start with it.
    // This detects broken/tampered distribution files, not a same-user malware boundary.
    internal static void VerifyPackage(string archivePath, string directory, string expectedHash)
    {
        using var archiveFile = File.OpenRead(archivePath);
        if (!Convert.ToHexString(SHA256.HashData(archiveFile)).Equals(expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("The bundled extension archive failed verification.");
        archiveFile.Position = 0;
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read);
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !expectedFiles.Add(path))
                throw new InvalidDataException("Invalid bundled extension path.");
            using var installed = File.OpenRead(path);
            using var original = entry.Open();
            if (installed.Length != entry.Length || !SHA256.HashData(installed).AsSpan().SequenceEqual(SHA256.HashData(original)))
                throw new InvalidDataException("The unpacked extension failed verification.");
        }
        if (!expectedFiles.Contains(Path.Combine(root, "manifest.json")) ||
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path =>
                !expectedFiles.Contains(path) && !IsGeneratedRuleset(root, path)))
            throw new InvalidDataException("The extension contains missing or unexpected files.");
    }

    private static bool IsGeneratedRuleset(string root, string path)
    {
        // Chromium writes compiled DNR data here when loading an unpacked extension.
        var prefix = Path.Combine(root, "_metadata", "generated_indexed_rulesets", "_ruleset");
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            path.Length > prefix.Length && path.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    internal static async Task InstallAsync(CoreWebView2Profile profile)
    {
        // Reinstall the same verified path after legacy worker cleanup. WebView2 keeps
        // the path-derived identity and profile settings; no store/update service is used.
        var extension = await profile.AddBrowserExtensionAsync(UbolDirectory).WaitAsync(TimeSpan.FromSeconds(30));
        if (string.IsNullOrWhiteSpace(extension.Id) || !extension.IsEnabled)
            throw new InvalidOperationException("The built-in extension is not available.");
    }
}
