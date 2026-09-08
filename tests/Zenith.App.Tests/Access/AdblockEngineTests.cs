using Zenith.App.Filtering;
using Zenith.Core.Filtering;

namespace Zenith.App.Tests.Access;

public sealed class AdblockEngineTests
{
    [Theory]
    [InlineData("https://ads.example/banner.js", "https://news.example/", ResourceKind.Script, true)]
    [InlineData("https://ads.example/banner.js", "https://news.example/", ResourceKind.Image, false)]
    [InlineData("https://ads.example/banner.js", "https://ads.example/", ResourceKind.Script, false)]
    [InlineData("https://ads.example/allowed.js", "https://news.example/", ResourceKind.Script, false)]
    [InlineData("https://ads.example.evil.test/banner.js", "https://news.example/", ResourceKind.Script, false)]
    [InlineData("https://news.example/local-ad.png", "https://news.example/", ResourceKind.Image, true)]
    [InlineData("https://news.example/api?q=test&value=%E2%9C%93", "https://news.example/", ResourceKind.Fetch, false)]
    public void MaintainedEngineHandlesPatternsTypesPartiesAndExceptions(string url, string source, ResourceKind kind, bool blocked)
    {
        using var engine = new AdblockEngine("||ads.example^$script,third-party\n@@||ads.example/allowed.js$script\n/local-ad.png$image");
        Assert.Equal(blocked, engine.Blocks(new(url, source, kind)));
    }

    [Fact]
    public void CosmeticsHonorDomainAndGenericExceptionsAndNeverReturnScriptlets()
    {
        using var engine = new AdblockEngine("##.advert\nnews.example##.sponsor\nnews.example#@#.advert\nnews.example##+js(set, unsafe, true)\n@@||private.example^$elemhide");
        var news = engine.Cosmetics("https://news.example/", ["advert", "sponsor"], [], []);
        Assert.Contains(".sponsor", news);
        Assert.DoesNotContain(".advert", news);
        Assert.DoesNotContain("unsafe", news);
        Assert.Contains(".advert", engine.Cosmetics("https://elsewhere.example/", ["advert"], [], []));
        Assert.DoesNotContain(".sponsor", engine.Cosmetics("https://elsewhere.example/", ["sponsor"], [], []));
        Assert.Equal("", engine.Cosmetics("https://private.example/", ["advert"], [], []));
    }

    [Fact]
    public void BundledListsActuallyBlockAndHideAds()
    {
        using var engine = new AdblockEngine(AdblockEngine.ReadAsset("easylist.txt") + "\n" + AdblockEngine.ReadAsset("easyprivacy.txt"));
        Assert.True(engine.Blocks(new("https://ad.doubleclick.net/advert.js", "https://example.org/", ResourceKind.Script)));
        Assert.False(engine.Blocks(new("https://example.org/essential.js", "https://example.org/", ResourceKind.Script)));
        Assert.NotEmpty(engine.Cosmetics("https://example.org/", ["adsbygoogle"], [], []));
    }

    [Fact]
    public void AdvancedActionsDoNotBecomeExecutableCosmeticOutput()
    {
        using var engine = new AdblockEngine("example.org##+js(set, sentinel, true)\nexample.org##.advert:style(background: url(https://unsafe.example/))\nexample.org##.plain-ad");
        var css = engine.Cosmetics("https://example.org/", ["advert", "plain-ad"], [], []);
        Assert.Contains(".plain-ad", css);
        Assert.DoesNotContain("sentinel", css);
        Assert.DoesNotContain("background", css);
        Assert.DoesNotContain("unsafe.example", css);
    }
}
