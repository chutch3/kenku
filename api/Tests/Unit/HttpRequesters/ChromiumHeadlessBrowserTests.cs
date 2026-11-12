using API.HttpRequesters;
using Xunit;

namespace API.Tests.Unit.HttpRequesters;

/// <summary>Runs only where a Chromium binary is configured (CHROME_BIN / PUPPETEER_EXECUTABLE_PATH, as in the
/// production image); elsewhere the test reports as skipped rather than passing without having run.</summary>
public sealed class RequiresChromiumFactAttribute : FactAttribute
{
    public RequiresChromiumFactAttribute()
    {
        string? path = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH") ?? Environment.GetEnvironmentVariable("CHROME_BIN");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Skip = "No Chromium binary configured (set CHROME_BIN).";
    }
}

/// <summary>Writes throwaway HTML pages to a temp directory and hands back their file:// URLs.</summary>
public sealed class LocalTestPages : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("kenku-chromium-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    public string Page(string name, string script, string body = "")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, $"<html><body style=\"margin:0\">{body}<script>{script}</script></body></html>");
        return new Uri(path).AbsoluteUri;
    }

    /// <summary>A virtualized long strip like Comix's reader: a fixed-height scrolling container that only mounts
    /// the images overlapping (or adjacent to) its viewport.</summary>
    public string VirtualizedStrip(int pageCount) => Page("strip.html", $$"""
        const main = document.querySelector('main'), strip = document.getElementById('strip'), H = 1200;
        const render = () => {
          const first = Math.max(0, Math.floor(main.scrollTop / H) - 1);
          const last = Math.min({{pageCount}} - 1, Math.floor((main.scrollTop + main.clientHeight) / H) + 1);
          strip.innerHTML = '';
          for (let i = first; i <= last; i++)
            strip.insertAdjacentHTML('beforeend', `<img class="rpage-page__img" src="https://img.invalid/p${i}.webp" style="position:absolute;top:${i * H}px;height:${H}px">`);
        };
        main.addEventListener('scroll', render);
        setTimeout(render, 300);
        """, $"""<main class="rpage-main rpage-main--long-strip" style="height:100vh;overflow-y:auto"><div id="strip" style="position:relative;height:{pageCount * 1200}px"></div></main>""");
}

/// <summary>A throwaway local HTTP server (a real http:// origin, which cookies need and file:// lacks) that answers
/// each request path with the handler's status, content type and body.</summary>
public sealed class LocalHttpServer : IDisposable
{
    private System.Net.HttpListener _listener = new();
    private string _prefix = "";

    public static LocalHttpServer Start(Func<string, (int Status, string ContentType, string Body)> handler)
        => StartWithRequest(request => handler(request.Url!.AbsolutePath));

    public static LocalHttpServer StartWithRequest(Func<System.Net.HttpListenerRequest, (int Status, string ContentType, string Body)> handler)
    {
        var server = new LocalHttpServer();
        for (int port = 47000; port < 47200 && !server._listener.IsListening; port++)
        {
            try
            {
                server._prefix = $"http://127.0.0.1:{port}";
                server._listener.Prefixes.Clear();
                server._listener.Prefixes.Add(server._prefix + "/");
                server._listener.Start();
            }
            catch (System.Net.HttpListenerException)
            {
                server._listener.Close();
                server._listener = new System.Net.HttpListener();
            }
        }
        if (!server._listener.IsListening)
            throw new InvalidOperationException("No free local test port.");
        _ = Task.Run(async () =>
        {
            while (server._listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await server._listener.GetContextAsync(); }
                catch (Exception) { return; }
                var (status, contentType, body) = handler(ctx.Request);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = contentType;
                await ctx.Response.OutputStream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body));
                ctx.Response.Close();
            }
        });
        return server;
    }

    public string Url(string path) => _prefix + path;

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}

public class ChromiumHeadlessBrowserTests : IDisposable
{
    private readonly LocalTestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [RequiresChromiumFact]
    public async Task ImportedSession_SharesCookiesAndUserAgentOnlyWithItsNamedRenders()
    {
        using LocalHttpServer server = LocalHttpServer.Start(_ => (200, "text/html", """
            <html><body><p id="state"></p><script>
            document.querySelector('#state').textContent = document.cookie + '|' + navigator.userAgent;
            </script></body></html>
            """));
        await using ChromiumHeadlessBrowser browser = new(new KenkuSettings { UserAgent = "Kenku-test" });
        await browser.ImportSessionAsync("solved", "Solver-test",
            [new PuppeteerSharp.CookieParam { Name = "passed", Value = "yes", Domain = "127.0.0.1", Path = "/" }]);

        string solved = await browser.RenderHtmlAsync(server.Url("/"), waitForSelector: "#state", session: "solved");
        string isolated = await browser.RenderHtmlAsync(server.Url("/"), waitForSelector: "#state");

        Assert.Contains("passed=yes|Solver-test", solved);
        Assert.Contains(">|Kenku-test</p>", isolated);
        await Assert.ThrowsAsync<InvalidOperationException>(() => browser.ImportSessionAsync("solved", "Other", []));
    }

    [Fact]
    public void LaunchArgs_DropsSingleProcess_WhichCannotHostAnIsolatedContextPerRender()
    {
        // The production image used to pass --single-process; with it, Chromium closes the target on every
        // CreateBrowserContext, so every render would fail.
        string[] args = ChromiumHeadlessBrowser.LaunchArgs("--no-sandbox --disable-gpu --no-zygote --single-process");

        Assert.Equal(["--no-sandbox", "--disable-gpu", "--no-zygote"], args);
    }

    [Fact]
    public void LaunchArgs_DefaultsWhenNoneConfigured()
    {
        Assert.Equal(["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu"],
            ChromiumHeadlessBrowser.LaunchArgs(null));
    }

    [RequiresChromiumFact]
    public async Task RenderHtmlAsync_DoesNotCarryPageStateFromOneRenderToTheNext()
    {
        // The browser is a shared singleton; a site that persists state (e.g. Comix restoring the last search
        // keyword from localStorage over the URL's) must not see an earlier render's state.
        string url = _pages.Page("counter.html", """
            const n = Number(localStorage.getItem('n') ?? 0) + 1;
            localStorage.setItem('n', n);
            document.body.insertAdjacentHTML('beforeend', `<p id="count">${n}</p>`);
            """);
        await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

        string first = await browser.RenderHtmlAsync(url, waitForSelector: "#count");
        string second = await browser.RenderHtmlAsync(url, waitForSelector: "#count");

        Assert.Contains("""<p id="count">1</p>""", first);
        Assert.Contains("""<p id="count">1</p>""", second);
    }

    [RequiresChromiumFact]
    public async Task RenderHtmlAsync_WaitsForAnElementThatAppearsAfterLoad()
    {
        string url = _pages.Page("late.html", """
            setTimeout(() => document.body.insertAdjacentHTML('beforeend', '<p id="late">ready</p>'), 1500);
            """);
        await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

        string html = await browser.RenderHtmlAsync(url, waitForSelector: "#late");

        Assert.Contains("""<p id="late">ready</p>""", html);
    }

    [RequiresChromiumFact]
    public async Task DrivePageAsync_GivesTheScriptWorkingPagePrimitives()
    {
        string url = _pages.VirtualizedStrip(pageCount: 6);
        await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

        var (appeared, firstMounted, documentAtEnd, stripSteps) = await browser.DrivePageAsync(url, async page =>
        {
            bool appeared = await page.WaitForSelectorAsync("main img");
            IReadOnlyList<string> firstMounted = await page.QueryAttributesAsync("main img", "src");
            bool documentAtEnd = await page.ScrollByAsync(null, 800, 0);
            int steps = 1;
            while (!await page.ScrollByAsync("main", 800, 50))
                steps++;
            return (appeared, firstMounted, documentAtEnd, steps);
        });

        Assert.True(appeared);
        Assert.Equal(["https://img.invalid/p0.webp", "https://img.invalid/p1.webp"], firstMounted);
        Assert.True(documentAtEnd); // the document itself does not scroll here; only the strip does
        Assert.Equal(9, stripSteps); // ⌈(6 × 1200 − 600px default viewport) / 800⌉ — the strip reports its end
    }

    [RequiresChromiumFact]
    public async Task RenderHtmlAsync_LogsAPagesFailedDataRequests()
    {
        // A site that gates its data API (e.g. Comix answering 403 captcha_required) still renders its page
        // shell; the failed background request is the only evidence of why the content never appeared.
        using var server = LocalHttpServer.Start(path => path == "/api/chapters"
            ? (403, "application/json", """{"error":"captcha_required","challenge":"/@waf/challenge"}""")
            : (200, "text/html", """<html><body><p id="shell">shell</p><script>fetch('/api/chapters?page=1').then(() => document.body.insertAdjacentHTML('beforeend', '<p id="done">done</p>'));</script></body></html>"""));

        var appender = new log4net.Appender.MemoryAppender();
        var repo = (log4net.Repository.Hierarchy.Hierarchy)log4net.LogManager.GetRepository(typeof(ChromiumHeadlessBrowser).Assembly);
        var logger = (log4net.Repository.Hierarchy.Logger)repo.GetLogger(typeof(ChromiumHeadlessBrowser).FullName!);
        logger.AddAppender(appender);
        logger.Level = log4net.Core.Level.All;
        repo.Configured = true;
        try
        {
            await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

            await browser.RenderHtmlAsync(server.Url("/"), waitForSelector: "#done");

            Assert.Contains(appender.GetEvents(), e =>
                e.Level == log4net.Core.Level.Warn
                && e.RenderedMessage.Contains(server.Url("/api/chapters"))
                && e.RenderedMessage.Contains("403")
                && e.RenderedMessage.Contains("captcha_required"));
        }
        finally
        {
            logger.RemoveAppender(appender);
        }
    }

    [RequiresChromiumFact]
    public async Task ImportSessionAsync_SharesItsCookiesWithRendersInThatSessionOnly()
    {
        // Renders naming a session carry that session's cookies; every other render stays isolated.
        using var server = LocalHttpServer.Start(_ =>
            (200, "text/html", """<html><body><script>document.body.insertAdjacentHTML('beforeend', '<p id="c">' + (document.cookie || 'none') + '</p>')</script></body></html>"""));
        await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

        await browser.ImportSessionAsync("site", "Kenku/2.0 (Unix; x64)",
            [new PuppeteerSharp.CookieParam { Name = "passed", Value = "yes", Url = server.Url("/") }]);
        string inSession = await browser.RenderHtmlAsync(server.Url("/data"), waitForSelector: "#c", session: "site");
        string isolated = await browser.RenderHtmlAsync(server.Url("/data"), waitForSelector: "#c");

        Assert.Contains("""<p id="c">passed=yes</p>""", inSession);
        Assert.Contains("""<p id="c">none</p>""", isolated);
    }
}
