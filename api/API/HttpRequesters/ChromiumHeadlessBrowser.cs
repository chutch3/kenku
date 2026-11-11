using API.HttpRequesters.Interfaces;
using log4net;
using PuppeteerSharp;

namespace API.HttpRequesters;

/// <summary>
/// The single owner of the headless Chromium process. Registered as a DI singleton so the whole app
/// shares one browser and one global page-concurrency cap; connectors reach it through
/// <see cref="IHeadlessBrowser"/> rather than launching their own.
/// </summary>
internal sealed class ChromiumHeadlessBrowser : IHeadlessBrowser, IAsyncDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChromiumHeadlessBrowser));
    private readonly KenkuSettings _settings;
    private readonly object _lock = new();
    private IBrowser? _browser;
    private long _activePages = 0;
    private const int MaxPages = 2;
    private const int SelectorTimeoutMs = 15000;

    public ChromiumHeadlessBrowser(KenkuSettings settings)
    {
        _settings = settings;
    }

    public async Task<string> RenderHtmlAsync(string url, string? referrer = null, string? waitForSelector = null,
        CancellationToken cancellationToken = default)
    {
        EnsureBrowserInitialized();
        if (_browser is null)
        {
            Log.Warn("Browser not initialized; returning empty HTML.");
            return string.Empty;
        }

        while (Interlocked.Read(ref _activePages) >= MaxPages)
        {
            await Task.Delay(50, cancellationToken);
        }

        Interlocked.Increment(ref _activePages);
        // Each render gets its own browser context so cookies and storage never carry from one render to
        // the next — the browser is a shared singleton, and some sites restore state over the URL's.
        IBrowserContext? context = null;
        try
        {
            context = await _browser.CreateBrowserContextAsync();
            IPage page = await NewPageAsync(context, referrer);

            bool success = false;
            Exception? lastEx = null;
            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    await page.GoToAsync(url);
                    success = true;
                    Log.DebugFormat("Page loaded on retry {0}. {1}", retry + 1, url);
                    break;
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex.Message.Contains("Timeout"))
                {
                    lastEx = ex;
                    Log.WarnFormat("Timeout for {0} on retry {1}; waiting {2}ms...", url, retry + 1, 1000 * (retry + 1));
                    await Task.Delay(1000 * (retry + 1), cancellationToken);
                    await page.CloseAsync();
                    page = await NewPageAsync(context, referrer);
                }
            }

            if (!success)
            {
                Log.ErrorFormat("Chromium request failed for {0} after retries: {1}", url, lastEx?.Message);
                return string.Empty;
            }

            if (waitForSelector is null)
            {
                await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);");
                await Task.Delay(2000, cancellationToken);
            }
            else
            {
                try
                {
                    await page.WaitForSelectorAsync(waitForSelector, new WaitForSelectorOptions { Timeout = SelectorTimeoutMs });
                }
                catch (WaitTaskTimeoutException)
                {
                    Log.WarnFormat("No element matching '{0}' appeared on {1} within {2}ms.", waitForSelector, url, SelectorTimeoutMs);
                }
            }

            string html = await page.GetContentAsync();
            Log.DebugFormat("Chromium rendered {0} successfully.", url);
            return html;
        }
        finally
        {
            if (context != null)
            {
                try
                {
                    await context.CloseAsync();
                }
                catch (Exception ex)
                {
                    Log.WarnFormat("Error closing browser context: {0}", ex.Message);
                }
            }
            Interlocked.Decrement(ref _activePages);
        }
    }

    private async Task<IPage> NewPageAsync(IBrowserContext context, string? referrer)
    {
        IPage page = await context.NewPageAsync();
        await page.SetUserAgentAsync(_settings.UserAgent);
        if (!string.IsNullOrEmpty(referrer))
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { { "Referer", referrer } });
        return page;
    }

    private void EnsureBrowserInitialized()
    {
        if (_browser != null) return;

        lock (_lock)
        {
            if (_browser != null) return;
            try
            {
                Log.Debug("Starting Chromium init.");

                string? localPath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH") ?? Environment.GetEnvironmentVariable("CHROME_BIN");
                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    throw new InvalidOperationException($"Local Chromium binary not found at {localPath}. Set PUPPETEER_EXECUTABLE_PATH or CHROME_BIN.");
                }
                Log.InfoFormat("Using local Chromium at {0}", localPath);

                LaunchOptions launchOptions = new()
                {
                    Headless = true,
                    Timeout = 60000,
                    ExecutablePath = localPath,
                    Args = Environment.GetEnvironmentVariable("PUPPETEER_ARGS")?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? new[]
                    {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu"
                    }
                };
                _browser = Puppeteer.LaunchAsync(launchOptions, null).GetAwaiter().GetResult();

                Log.Debug("Chromium browser initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to initialize Chromium browser: {ex.Message}");
                _browser = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            try
            {
                await _browser.CloseAsync();
                await _browser.DisposeAsync();
                Log.Debug("Chromium browser disposed.");
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Error disposing browser: {0}", ex.Message);
            }
            _browser = null;
        }
    }
}
