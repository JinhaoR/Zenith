using System.Net;
using System.Net.Http;
using Zenith.App.Filtering;

namespace Zenith.App.Tests.Navigation;

internal sealed class AdblockWebViewFixture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = request.RequestUri!.AbsoluteUri == AdblockService.EasyListUrl ? "easylist.txt" : "easyprivacy.txt";
        var text = AdblockEngine.ReadAsset(name) + """

            ##.zenith-test-ad
            ##.zenith-dynamic-ad
            github.com##.zenith-test-ad
            ##.zenith-generic-ad
            github.com#@#.zenith-generic-ad
            embedded.example##.zenith-frame-ad
            github.com##.zenith-main-ad
            ||ads.zenith-test.example^$script,image,subdocument,xmlhttprequest,third-party
            @@||ads.zenith-test.example/allowed$xmlhttprequest
            @@||blocked.example^
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(text) });
    }

    internal static string Page(string host) => host switch
    {
        "github.com" => """
            <html><head><title>Filter test</title></head><body>
            <div id="advert" class="zenith-test-ad">advert</div>
            <div id="exception" class="zenith-generic-ad">exception</div>
            <div id="scoped" class="zenith-main-ad">main only</div>
            <div id="content">ordinary content</div>
            <iframe name="cosmetic-child" src="https://embedded.example/filter-test"></iframe>
            </body></html>
            """,
        "embedded.example" => """
            <html><body><div id="advert" class="zenith-test-ad">ad</div>
            <div id="exception" class="zenith-generic-ad">generic</div>
            <div id="scoped" class="zenith-frame-ad">frame only</div>
            <iframe name="cosmetic-nested" src="https://nested.example/filter-test"></iframe></body></html>
            """,
        _ => """
            <html><body><div id="advert" class="zenith-test-ad">ad</div>
            <div id="scoped" class="zenith-frame-ad">not this frame</div></body></html>
            """
    };
}
