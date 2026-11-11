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

/// <summary>A throwaway local HTTP server (a real http:// origin, unlike file://) that answers each request path with the
/// handler's status, content type and body.</summary>
public sealed class LocalHttpServer : IDisposable
{
    private readonly System.Net.HttpListener _listener = new();
    private string _prefix = "";

    public static LocalHttpServer Start(Func<string, (int Status, string ContentType, string Body)> handler)
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
            catch (System.Net.HttpListenerException) { }
        }
        _ = Task.Run(async () =>
        {
            while (server._listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await server._listener.GetContextAsync(); }
                catch (Exception) { return; }
                var (status, contentType, body) = handler(ctx.Request.Url!.AbsolutePath);
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
    private readonly string _dir = Directory.CreateTempSubdirectory("kenku-chromium-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Page(string name, string script)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, $"<html><body><script>{script}</script></body></html>");
        return new Uri(path).AbsoluteUri;
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
        // The browser is a shared singleton; a site that persists state (e.g. one restoring the last search
        // keyword from localStorage over the URL's) must not see an earlier render's state.
        string url = Page("counter.html", """
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
        string url = Page("late.html", """
            setTimeout(() => document.body.insertAdjacentHTML('beforeend', '<p id="late">ready</p>'), 1500);
            """);
        await using var browser = new ChromiumHeadlessBrowser(new KenkuSettings());

        string html = await browser.RenderHtmlAsync(url, waitForSelector: "#late");

        Assert.Contains("""<p id="late">ready</p>""", html);
    }

    [RequiresChromiumFact]
    public async Task RenderHtmlAsync_LogsAPagesFailedDataRequests()
    {
        // A site that gates its data API (e.g. answering 403 captcha_required) still renders its page shell; the
        // failed background request is the only evidence of why the content never appeared.
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
}
