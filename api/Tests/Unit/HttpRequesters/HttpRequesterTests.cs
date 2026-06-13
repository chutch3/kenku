using System.Net;
using API;
using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

public class HttpRequesterTests
{
    private static HttpRequester CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var rateLimit = new RateLimitHandler(settings, new FakeHttpMessageHandler(handler));
        return new HttpRequester(rateLimit, settings);
    }

    [Fact]
    public void Constructs_WithACustomUserAgent_TheTypedParserWouldReject()
    {
        // Users can set any User-Agent; a reddit-style value ("kenku:discovery (…)") trips the typed
        // header parser, which would throw at requester construction and break every outbound call.
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        settings.SetUserAgent("kenku:discovery (self-hosted manga manager)");
        var rateLimit = new RateLimitHandler(settings, new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var ex = Record.Exception(() => new HttpRequester(rateLimit, settings));

        Assert.Null(ex);
    }

    [Fact]
    public async Task MakeRequest_Success_ReturnsResponse()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello")
        });

        HttpResponseMessage response = await client.MakeRequest("http://example.test/a", RequestType.Default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MakeRequest_NonSuccessWithoutCloudflare_ReturnsInternalServerError()
    {
        // Exercises the error-logging branch (which previously blocked on .Result). Must complete and
        // return 500 without hanging or throwing.
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("not found")
        });

        HttpResponseMessage response = await client.MakeRequest("http://example.test/missing", RequestType.MangaInfo);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
