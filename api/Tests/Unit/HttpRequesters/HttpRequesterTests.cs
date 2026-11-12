using System.Net;
using API;
using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

public class HttpRequesterTests
{
    [Theory]
    [InlineData(200, false)]
    [InlineData(403, true)]
    [InlineData(404, false)]
    [InlineData(429, false)]
    public async Task SolverIsAFallbackOnlyForChallenges(int status, bool shouldSolve)
    {
        int solverCalls = 0;
        KenkuSettings settings = new() { ChallengeSolverUrl = "http://solver.test" };
        using HttpClient solverHttp = new(new FakeHttpMessageHandler(_ =>
        {
            solverCalls++;
            return ChallengeSolverClientTests.Solved();
        }));
        using ChallengeSolverClient solver = new(solverHttp, settings);
        using RateLimitHandler rate = new(settings, new FakeHttpMessageHandler(_ =>
        {
            HttpResponseMessage response = new((HttpStatusCode)status)
            {
                Content = new StringContent(shouldSolve ? "<html><title>Just a moment...</title></html>" : "ordinary response")
            };
            response.Headers.Server.ParseAdd("cloudflare");
            return response;
        }));
        HttpRequester requester = new(rate, settings, solver);
        using HttpResponseMessage result = await requester.MakeRequest("https://example.test/a", RequestType.SeriesInfo);
        Assert.Equal(shouldSolve ? 1 : 0, solverCalls);
        if (shouldSolve)
            Assert.Equal("<p>solved</p>", await result.Content.ReadAsStringAsync());
    }

    private static HttpRequester CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var rateLimit = new RateLimitHandler(settings, new FakeHttpMessageHandler(handler));
        return new HttpRequester(rateLimit, settings, new ChallengeSolverClient(settings));
    }

    [Fact]
    public void Constructs_WithACustomUserAgent_TheTypedParserWouldReject()
    {
        // Users can set any User-Agent; a reddit-style value ("kenku:discovery (…)") trips the typed
        // header parser, which would throw at requester construction and break every outbound call.
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        settings.SetUserAgent("kenku:discovery (self-hosted manga manager)");
        var rateLimit = new RateLimitHandler(settings, new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var ex = Record.Exception(() => new HttpRequester(rateLimit, settings, new ChallengeSolverClient(settings)));

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
    public async Task MakeRequest_NonSuccessWithoutCloudflare_ReturnsTheServersStatus()
    {
        // Exercises the error-logging branch (which previously blocked on .Result): it must complete without
        // hanging or throwing, and report the server's own status so a dead link (404) or a rate limit (429)
        // isn't mistaken for a server error.
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("not found")
        });

        HttpResponseMessage response = await client.MakeRequest("http://example.test/missing", RequestType.SeriesInfo);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
