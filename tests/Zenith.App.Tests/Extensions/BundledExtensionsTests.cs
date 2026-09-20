using System.IO.Compression;
using System.Security.Cryptography;
using Zenith.App.Extensions;

namespace Zenith.App.Tests.Extensions;

public sealed class BundledExtensionsTests
{
    [Fact]
    public void ShippedArchiveAndAllExtractedFilesMatchPinnedPackage() => BundledExtensions.VerifyPackage();

    [Theory]
    [InlineData("archive")]
    [InlineData("modified")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("traversal")]
    public void DamagedOrUnexpectedExtensionFilesAreRejected(string damage)
    {
        var root = Directory.CreateTempSubdirectory("Zenith-Package-");
        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(root.FullName, "unpacked")).FullName;
            var zip = Path.Combine(root.FullName, "extension.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry(damage == "traversal" ? "../manifest.json" : "manifest.json").Open());
                writer.Write("{}");
            }
            File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
            if (damage == "archive") File.AppendAllText(zip, "modified");
            if (damage == "modified") File.WriteAllText(Path.Combine(directory, "manifest.json"), "[]");
            if (damage == "missing") File.Delete(Path.Combine(directory, "manifest.json"));
            if (damage == "extra") File.WriteAllText(Path.Combine(directory, "extra.js"), "extra");
            if (damage == "missing")
                Assert.Throws<FileNotFoundException>(() => BundledExtensions.VerifyPackage(zip, directory, hash));
            else
                Assert.Throws<InvalidDataException>(() => BundledExtensions.VerifyPackage(zip, directory, hash));
        }
        finally { root.Delete(true); }
    }
}
