using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zenith.App.Navigation;

/// <summary>Only bounded PNG bytes from WebView2; no URI loading or filesystem resolution.</summary>
internal static class FaviconDecoder
{
    private const int MaximumBytes = 512 * 1024;

    internal static async Task<ImageSource?> DecodeAsync(Stream source)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await source.ReadAsync(chunk)) != 0)
        {
            if (buffer.Length + read > MaximumBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.GetBuffer();
        if (buffer.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return null;
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is 0 or > 512 || height is 0 or > 512) return null;
        buffer.Position = 0;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 32;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
    }
}
