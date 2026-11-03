using System.Net;
using API;
using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

public class FlareSolverrRequesterTests
{
    // Realistic solved response shape (FlareSolverr and its API-compatible replacement Byparr both
    // return this): a browser-rendered page plus the cookies/UA that got past the challenge.
    private const string SolvedResponse = """
        {"status":"ok","message":"Success","solution":{"url":"https://example.test/page","status":200,
        "cookies":[{"name":"cf_clearance","value":"abc123","domain":"example.test"}],
        "userAgent":"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36",
        "headers":{},"response":"<html>page</html>"}}
        """;

    private static FlareSolverrRequester CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath(), FlareSolverrUrl = "http://flaresolverr.test" };
        return new FlareSolverrRequester(new HttpClient(new FakeHttpMessageHandler(handler)), settings);
    }

    [Fact]
    public async Task MakeRequest_DefaultRequestType_ReturnsTheRenderedPage()
    {
        var client = CreateClient(req =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) };
        });

        HttpResponseMessage response = await client.MakeRequest("https://example.test/page", RequestType.Default);

        Assert.Equal("<html>page</html>", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(RequestType.SeriesImage)]
    [InlineData(RequestType.SeriesCover)]
    public async Task MakeRequest_BinaryRequestTypes_FetchTheRealBytesWithTheSolvedCookieInsteadOfTheRenderedWrapper(
        RequestType requestType)
    {
        // A browser navigating straight to an image URL renders an HTML "view image" wrapper, not the
        // bytes — real for ComicHubFree et al. once the Cloudflare challenge is solved. The fix: take the
        // cookie the solve just earned and fetch the asset directly, the same way a browser's own
        // follow-up image request would, rather than returning the wrapper as if it were the image.
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0];
        int call = 0;
        var client = CreateClient(req =>
        {
            call++;
            if (call == 1)
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) };
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.Equal("https://example.test/page/1.jpg", req.RequestUri!.ToString());
            Assert.Equal("cf_clearance=abc123", req.Headers.GetValues("Cookie").Single());
            // The typed UserAgent collection (not GetValues, which re-joins a product/comment token
            // list with ", " for generic enumeration — cosmetic only, confirmed against a real
            // loopback server to send the untouched string either way) reconstructs the original.
            Assert.Equal(
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36",
                req.Headers.UserAgent.ToString());
            Assert.Equal("https://example.test/reader", req.Headers.Referrer!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jpegBytes)
            };
        });

        HttpResponseMessage response = await client.MakeRequest(
            "https://example.test/page/1.jpg", requestType, referrer: "https://example.test/reader");

        Assert.Equal(2, call);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(jpegBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MakeRequest_BinaryRequestType_NoReferrer_StillFetchesTheRealBytes()
    {
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0];
        int call = 0;
        var client = CreateClient(req =>
        {
            call++;
            if (call == 1)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) };

            Assert.Null(req.Headers.Referrer);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(jpegBytes) };
        });

        HttpResponseMessage response = await client.MakeRequest(
            "https://example.test/page/1.jpg", RequestType.SeriesImage);

        Assert.Equal(jpegBytes, await response.Content.ReadAsByteArrayAsync());
    }
}
