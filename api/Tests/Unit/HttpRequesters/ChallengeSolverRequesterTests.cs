using System.Net;
using API;
using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

public class ChallengeSolverRequesterTests
{
    // Realistic solved response shape (Byparr and other request.get-compatible solvers
    // return this): a browser-rendered page plus the cookies/UA that got past the challenge.
    private const string SolvedResponse = """
        {"status":"ok","message":"Success","solution":{"url":"https://example.test/page","status":200,
        "cookies":[{"name":"cf_clearance","value":"abc123","domain":"example.test"}],
        "userAgent":"Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36",
        "headers":{},"response":"<html>page</html>"}}
        """;

    private static ChallengeSolverRequester CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath(), ChallengeSolverUrl = "http://solver.test" };
        return new ChallengeSolverRequester(new HttpClient(new FakeHttpMessageHandler(handler)), settings);
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
    [InlineData(RequestType.Archive)]
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

    [Fact]
    public async Task ARejectedSolutionsCookiesLeaveTheSharedCookieJar()
    {
        // The site still challenges the solved session, so it's rejected; its clearance must not keep riding
        // along on every later request to that host from the shared transport cookie jar.
        var settings = new KenkuSettings { AppData = Path.GetTempPath(), ChallengeSolverUrl = "http://solver.test" };
        using var http = new HttpClient(new FakeHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) }
            : ChallengePage()));
        using var solver = new ChallengeSolverClient(http, settings);
        var jar = new CookieContainer();
        var requester = new ChallengeSolverRequester(http, solver, jar);

        using HttpResponseMessage response = await requester.MakeRequest("https://example.test/page/1.jpg", RequestType.SeriesImage);

        Assert.DoesNotContain(jar.GetCookies(new Uri("https://example.test/")), c => c.Name == "cf_clearance");
    }

    private static HttpResponseMessage ChallengePage()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<title>Just a moment...</title>") };
        response.Headers.Server.ParseAdd("cloudflare");
        return response;
    }

    [Fact]
    public async Task ACachedSessionChallengedDuringTheCooldown_IsStillDiscarded()
    {
        // The cooldown stops another solve; it must not stop the failed session from being dropped, or its
        // cookies keep riding along on every request to that host until it expires.
        var settings = new KenkuSettings { AppData = Path.GetTempPath(), ChallengeSolverUrl = "http://solver.test" };
        using var http = new HttpClient(new FakeHttpMessageHandler(req => req.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SolvedResponse) }
            : ChallengePage()));
        using var solver = new ChallengeSolverClient(http, settings);
        var jar = new CookieContainer();
        var requester = new ChallengeSolverRequester(http, solver, jar);
        const string url = "https://example.test/page";
        SolverSession cached = await solver.SolveAsync(url, null, default); // starts the cooldown
        cached.Apply(new HttpRequestMessage(HttpMethod.Get, url), jar);

        using HttpResponseMessage response = await requester.RecoverAsync(url, RequestType.Default, null, cached, default);

        Assert.Null(solver.GetSession(url));
        Assert.DoesNotContain(jar.GetCookies(new Uri("https://example.test/")), c => c.Name == "cf_clearance");
    }
}

