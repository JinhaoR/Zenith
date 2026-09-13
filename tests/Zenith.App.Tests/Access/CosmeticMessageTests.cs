using System.Text.Json;
using Zenith.App.Filtering;

namespace Zenith.App.Tests.Access;

public sealed class CosmeticMessageTests
{
    private const string Source = "https://example.com/page";
    private static CosmeticMessage Valid => new("zenith-cosmetics", "document-token", Source, ["ad"], [], []);

    [Fact]
    public void ValidPresentationHintsAreAccepted() => Assert.NotNull(CosmeticMessage.Read(JsonSerializer.Serialize(Valid), Source));

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    [InlineData("{\"kind\":\"change-policy\"}")]
    public void MalformedOrUnrelatedMessagesAreRejected(string json) => Assert.Null(CosmeticMessage.Read(json, Source));

    [Fact]
    public void ForgedOriginsAndUnsupportedDocumentsAreRejected()
    {
        Assert.Null(CosmeticMessage.Read(JsonSerializer.Serialize(Valid), "https://other.example/"));
        foreach (var source in new[] { "about:blank", "file:///C:/page.html", "data:text/html,hello" })
            Assert.Null(CosmeticMessage.Read(JsonSerializer.Serialize(Valid with { Url = source }), source));
    }

    [Fact]
    public void PayloadBoundsAreEnforced()
    {
        foreach (var query in new[] {
            Valid with { Token = "" }, Valid with { Token = new string('x', 65) },
            Valid with { Classes = new string[513] }, Valid with { Classes = [new string('x', 129)] },
            Valid with { Ids = null! }, Valid with { Hrefs = [null!] },
            Valid with { Hrefs = [new string('x', 513)] } })
            Assert.Null(CosmeticMessage.Read(JsonSerializer.Serialize(query), Source));
        Assert.Null(CosmeticMessage.Read(new string(' ', 256 * 1024 + 1), Source));
    }

    [Theory]
    [InlineData("https://example.com/page", true)]
    [InlineData("https://example.com/another-page", false)]
    [InlineData("https://other.example/page", false)]
    [InlineData("about:blank", false)]
    public void StaleTopLevelMessagesCannotReplyToAnotherPage(string current, bool expected) =>
        Assert.Equal(expected, CosmeticMessage.IsCurrentDocument(Source, current));
}
