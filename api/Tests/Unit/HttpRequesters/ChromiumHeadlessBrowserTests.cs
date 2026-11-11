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
}
