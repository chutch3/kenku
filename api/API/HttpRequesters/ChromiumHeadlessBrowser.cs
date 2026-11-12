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
    private const int MaxPages = 2;
    private readonly SemaphoreSlim _pageSlots = new(MaxPages, MaxPages);
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

        await _pageSlots.WaitAsync(cancellationToken);
        // Each render gets its own browser context so cookies and storage never carry from one render to
        // the next — the browser is a shared singleton, and some sites restore state over the URL's.
        IBrowserContext? context = null;
        List<Task> failedRequestLogs = [];
        try
        {
            context = await _browser.CreateBrowserContextAsync();
            IPage page = await NewPageAsync(context, referrer, failedRequestLogs);

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
                    page = await NewPageAsync(context, referrer, failedRequestLogs);
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
            // Let failed-request logging read its response bodies before the page they belong to goes away.
            Task[] pendingLogs;
            lock (failedRequestLogs)
                pendingLogs = failedRequestLogs.ToArray();
            await Task.WhenAll(pendingLogs).WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);

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
            _pageSlots.Release();
        }
    }

    private async Task<IPage> NewPageAsync(IBrowserContext context, string? referrer, List<Task> failedRequestLogs)
    {
        IPage page = await context.NewPageAsync();
        page.Response += (_, e) =>
        {
            lock (failedRequestLogs)
                failedRequestLogs.Add(LogFailedDataRequestAsync(e.Response));
        };
        await page.SetUserAgentAsync(_settings.UserAgent);
        if (!string.IsNullOrEmpty(referrer))
            await page.SetExtraHttpHeadersAsync(new Dictionary<string, string> { { "Referer", referrer } });
        return page;
    }

    // A site that gates its data API still renders its page shell, so a failed background request is often the
    // only evidence of why content never appeared (e.g. a 403 captcha_required).
    private static async Task LogFailedDataRequestAsync(IResponse response)
    {
        try
        {
            if ((int)response.Status < 400 || response.Request.ResourceType is not (ResourceType.Xhr or ResourceType.Fetch))
                return;
            string body;
            try
            {
                body = await response.TextAsync();
            }
            catch (Exception)
            {
                body = "(body unavailable — the page closed first)";
            }
            Log.WarnFormat("Data request {0} {1} returned {2}: {3}", response.Request.Method, response.Url.Split('?')[0],
                (int)response.Status, body.Length > 300 ? body[..300] : body);
        }
        catch (Exception ex)
        {
            Log.DebugFormat("Could not log a failed data request: {0}", ex.Message);
        }
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
                    Args = LaunchArgs(Environment.GetEnvironmentVariable("PUPPETEER_ARGS"))
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

    // --single-process can't host a separate browser context per render (Chromium closes the target on
    // creation), and that isolation is what keeps one render's cookies and storage out of the next.
    internal static string[] LaunchArgs(string? configured)
    {
        if (configured is null)
            return ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage", "--disable-gpu"];

        string[] args = configured.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Contains("--single-process"))
            Log.Warn("Ignoring --single-process in PUPPETEER_ARGS: it cannot host the isolated browser context each render uses.");
        return args.Where(a => a != "--single-process").ToArray();
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
