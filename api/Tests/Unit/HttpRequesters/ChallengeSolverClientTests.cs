using System.Net;
using System.Text.Json;
using API.HttpRequesters;

namespace API.Tests.Unit.HttpRequesters;

public class ChallengeSolverClientTests
{
    internal static HttpResponseMessage Solved(string domain = "example.test", string html = "<p>solved</p>") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            status = "ok", solution = new
            {
                status = 200, userAgent = "Solver/1.0", response = html,
                cookies = new[] { new { name = "clearance", value = "yes", domain, path = "/" } }
            }
        }))
    };

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(403, "cloudflare", false, "<title>Just a moment...</title>", true)]
    [InlineData(503, "cloudflare", false, "maintenance", false)]
    [InlineData(403, "cloudflare", false, "forbidden", false)]
    [InlineData(404, "cloudflare", false, "not found", false)]
    [InlineData(429, "cloudflare", false, "rate limited", false)]
    [InlineData(200, "cloudflare", false, "normal page", false)]
    [InlineData(403, "cloudflare", true, "", true)]
    public void DetectsChallengesWithoutTreatingOrdinaryErrorsAsChallenges(int status, string server, bool mitigated,
        string body, bool expected) => Assert.Equal(expected, BotChallenge.IsChallenge(status, server, mitigated, body));

    [Fact]
    public async Task ConcurrentFailuresShareOneSolve_AndSessionsAreOriginScoped()
    {
        int calls = 0;
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using HttpClient http = new(new FakeHttpMessageHandler(async (request, ct) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("http://solver.test/v1", request.RequestUri!.ToString());
            Assert.Contains("60000", await request.Content!.ReadAsStringAsync(ct));
            started.SetResult();
            await release.Task.WaitAsync(ct);
            return Solved();
        }));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" });
        Task<SolverSession> first = solver.SolveAsync("https://example.test/a", null, default);
        await started.Task;
        Task<SolverSession> second = solver.SolveAsync("https://example.test/b", null, default);
        release.SetResult();
        Assert.Same(await first, await second);
        Assert.Equal(1, calls);
        Assert.NotNull(solver.GetSession("https://example.test/c"));
        Assert.Null(solver.GetSession("http://example.test/a"));
        Assert.Null(solver.GetSession("https://other.test/a"));
    }

    [Fact]
    public async Task FailedOrRejectedSolutionsHaveACooldown_ThenCanRefresh()
    {
        int calls = 0;
        Clock clock = new();
        using HttpClient http = new(new FakeHttpMessageHandler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Solved()));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" }, clock);
        await Assert.ThrowsAsync<HttpRequestException>(() => solver.SolveAsync("https://example.test/a", null, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => solver.SolveAsync("https://example.test/b", null, default));
        Assert.Equal(1, calls);
        clock.Now += TimeSpan.FromMinutes(6);
        SolverSession solved = await solver.SolveAsync("https://example.test/b", null, default);
        solver.Reject("https://example.test/b", solved);
        Assert.Null(solver.GetSession("https://example.test/a"));
        await Assert.ThrowsAsync<HttpRequestException>(() => solver.SolveAsync("https://example.test/c", null, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExpiredSessionRefreshesOnTheNextChallenge()
    {
        int calls = 0;
        Clock clock = new();
        using HttpClient http = new(new FakeHttpMessageHandler(_ => { calls++; return Solved(); }));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" }, clock);
        SolverSession first = await solver.SolveAsync("https://example.test/a", null, default);
        clock.Now += TimeSpan.FromMinutes(31);
        Assert.Null(solver.GetSession("https://example.test/a"));
        SolverSession refreshed = await solver.SolveAsync("https://example.test/a", first, default);
        Assert.NotSame(first, refreshed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellationReachesTheSolver()
    {
        using CancellationTokenSource cancel = new();
        using HttpClient http = new(new FakeHttpMessageHandler(async (_, ct) =>
        {
            cancel.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Solved();
        }));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => solver.SolveAsync("https://example.test/a", null, cancel.Token));
    }

    [Theory]
    [InlineData("http://vpn:8888", "http://vpn:8888")]
    [InlineData("", null)]
    public async Task ThePerSolveProxyComesFromSettings(string configured, string? expectedHeader)
    {
        string? sentProxy = null;
        using HttpClient http = new(new FakeHttpMessageHandler(request =>
        {
            sentProxy = request.Headers.TryGetValues("X-Proxy-Server", out var values) ? values.Single() : null;
            return Solved();
        }));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test", ChallengeSolverProxyUrl = configured });

        await solver.SolveAsync("https://example.test/a", null, default);

        Assert.Equal(expectedHeader, sentProxy);
    }

    [Fact]
    public async Task RejectsCookiesForAnUnrelatedHost()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ => Solved("other.test")));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" });
        await Assert.ThrowsAsync<HttpRequestException>(() => solver.SolveAsync("https://example.test/a", null, default));
        Assert.Null(solver.GetSession("https://example.test/a"));
    }

    [Fact]
    public async Task CachedHtmlIsNeverReturnedForADifferentUrl()
    {
        using HttpClient solverHttp = new(new FakeHttpMessageHandler(_ => Solved(html: "page A")));
        using ChallengeSolverClient solver = new(solverHttp, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" });
        await solver.SolveAsync("https://example.test/a", null, default);
        using HttpClient sourceHttp = new(new FakeHttpMessageHandler(request =>
        {
            Assert.Equal("https://example.test/b", request.RequestUri!.ToString());
            Assert.Equal("clearance=yes", request.Headers.GetValues("Cookie").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("page B") };
        }));
        ChallengeSolverRequester requester = new(sourceHttp, solver, null);
        using HttpResponseMessage response = await requester.MakeRequest("https://example.test/b", RequestType.SeriesInfo);
        Assert.Equal("page B", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SolvedPageMayContainCloudflaresNormalJavascriptDetectionScript()
    {
        using HttpClient http = new(new FakeHttpMessageHandler(_ => Solved(html: "<html>real content<script src='/cdn-cgi/challenge-platform/scripts/jsd/main.js'></script></html>")));
        using ChallengeSolverClient solver = new(http, new KenkuSettings { ChallengeSolverUrl = "http://solver.test" });
        Assert.NotNull(await solver.SolveAsync("https://example.test/a", null, default));
    }
}
