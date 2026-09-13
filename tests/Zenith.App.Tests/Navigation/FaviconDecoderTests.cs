using System.Buffers.Binary;
using System.Text;
using Zenith.App.Navigation;

namespace Zenith.App.Tests.Navigation;

public sealed class FaviconDecoderTests
{
    [Theory]
    [InlineData("file:///C:/private.png")]
    [InlineData("https://attacker.example/collect")]
    [InlineData("\\\\attacker.example\\share\\icon")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    public async Task AddressesAndNonPngContentAreRejected(string value)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(value));
        Assert.Null(await FaviconDecoder.DecodeAsync(source));
    }

    [Fact]
    public async Task OversizedByteStreamIsRejected()
    {
        using var source = new MemoryStream(new byte[512 * 1024 + 1]);
        Assert.Null(await FaviconDecoder.DecodeAsync(source));
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(513u, 1u)]
    [InlineData(1u, uint.MaxValue)]
    public async Task OversizedDimensionsAreRejectedBeforeDecoding(uint width, uint height)
    {
        var png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        "IHDR"u8.CopyTo(png.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), height);
        using var source = new MemoryStream(png);
        Assert.Null(await FaviconDecoder.DecodeAsync(source));
    }
}
