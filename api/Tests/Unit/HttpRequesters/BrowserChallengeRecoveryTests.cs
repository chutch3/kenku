using API.HttpRequesters;

namespace API.Tests.Unit.HttpRequesters;

public class BrowserChallengeRecoveryTests
{
    [RequiresChromiumFact]
    public async Task DataApiChallengeRecoversAndReusesSessionInFreshContexts()
    {
        using LocalHttpServer server = LocalHttpServer.StartWithRequest(request =>
        {
            if (request.Url!.AbsolutePath == "/api")
                return request.Cookies["clearance"]?.Value == "yes" && request.UserAgent == "Solver/1.0"
                    ? (200, "application/json", "{\"ok\":true}")
                    : (403, "application/json", "{\"error\":\"captcha_required\"}");
            return (200, "text/html", """
                <html><body><script>
                fetch('/api').then(r => r.json()).then(data => {
                  document.body.innerHTML = '<p id="done">' + (data.ok ? 'real page' : 'blocked') + '</p>';
                });
                </script></body></html>
                """);
        });
        int calls = 0;
        KenkuSettings settings = new() { ChallengeSolverUrl = "http://solver.test" };
        using HttpClient http = new(new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return ChallengeSolverClientTests.Solved("127.0.0.1", "solver snapshot must not replace the real page");
        }));
        using ChallengeSolverClient solver = new(http, settings);
        await using ChromiumHeadlessBrowser browser = new(settings, solver);
        string first = await browser.RenderHtmlAsync(server.Url("/title"), waitForSelector: "#done");
        string second = await browser.RenderHtmlAsync(server.Url("/chapter"), waitForSelector: "#done");
        Assert.Contains("real page", first);
        Assert.Contains("real page", second);
        Assert.DoesNotContain("solver snapshot", first);
        Assert.Equal(1, calls);
    }

    [RequiresChromiumFact]
    public async Task PersistentChallengeGetsOneRecoveryAndThenCooldown()
    {
        int navigations = 0;
        using LocalHttpServer server = LocalHttpServer.Start(path =>
        {
            if (path == "/blocked") Interlocked.Increment(ref navigations);
            return (403, "application/json", "{\"error\":\"captcha_required\"}");
        });
        int calls = 0;
        KenkuSettings settings = new() { ChallengeSolverUrl = "http://solver.test" };
        using HttpClient http = new(new FakeHttpMessageHandler(_ =>
        {
            calls++;
            return ChallengeSolverClientTests.Solved("127.0.0.1");
        }));
        using ChallengeSolverClient solver = new(http, settings);
        await using ChromiumHeadlessBrowser browser = new(settings, solver);
        Assert.Equal("", await browser.RenderHtmlAsync(server.Url("/blocked")));
        Assert.Equal(2, navigations);
        Assert.Equal("", await browser.RenderHtmlAsync(server.Url("/blocked")));
        Assert.Equal(3, navigations);
        Assert.Equal(1, calls);
    }

    [RequiresChromiumFact]
    public async Task OrdinaryMissingContentDoesNotInvokeSolver()
    {
        using LocalHttpServer server = LocalHttpServer.Start(_ => (404, "text/html", "<p id='error'>not found</p>"));
        KenkuSettings settings = new() { ChallengeSolverUrl = "http://solver.test" };
        using HttpClient http = new(new FakeHttpMessageHandler(_ => throw new Exception("Must not solve an ordinary 404")));
        using ChallengeSolverClient solver = new(http, settings);
        await using ChromiumHeadlessBrowser browser = new(settings, solver);
        Assert.Contains("not found", await browser.RenderHtmlAsync(server.Url("/missing"), waitForSelector: "#error"));
    }
}
