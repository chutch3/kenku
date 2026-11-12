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
    private readonly ChallengeSolverClient _solver;
    private readonly object _lock = new();
    private IBrowser? _browser;
    private const int MaxPages = 2;
    private readonly SemaphoreSlim _pageSlots = new(MaxPages, MaxPages);
    private const int SelectorTimeoutMs = 15000;
    private readonly SemaphoreSlim _sessionsLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChromiumBrowserSession> _sessions = new();

    public ChromiumHeadlessBrowser(KenkuSettings settings, ChallengeSolverClient? solver = null)
    {
        _settings = settings;
        _solver = solver ?? new ChallengeSolverClient(settings);
    }

    public async Task<string> RenderHtmlAsync(string url, string? referrer = null, string? waitForSelector = null,
        string? session = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await WithPageAsync(url, referrer, session, async page =>
            {
                if (waitForSelector is null)
                {
                    await page.EvaluateExpressionAsync("window.scrollTo(0, document.body.scrollHeight);");
                    await Task.Delay(2000, cancellationToken);
                }
                else if (!await WaitForSelectorAsync(page, waitForSelector))
                {
                    Log.WarnFormat("No element matching '{0}' appeared on {1} within {2}ms.", waitForSelector, url, SelectorTimeoutMs);
                }

                string html = await page.GetContentAsync();
                Log.DebugFormat("Chromium rendered {0} successfully.", url);
                return html;
            }, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex.Message);
            return string.Empty;
        }
    }

    public Task<T> DrivePageAsync<T>(string url, Func<IBrowserPage, Task<T>> script, string? session = null,
        CancellationToken cancellationToken = default) =>
        WithPageAsync(url, referrer: null, session, page => script(new PuppeteerBrowserPage(page, cancellationToken)), cancellationToken);

    // Opens url in a fresh browser context — so cookies and storage never carry from one use to the next (the
    // browser is a shared singleton, and some sites restore state over the URL's) — or, when a named session is
    // open, in that session's context, under the global page cap, retrying load timeouts, then runs work against
    // the loaded page and tears down what it created.
    private async Task<T> WithPageAsync<T>(string url, string? referrer, string? session, Func<IPage, Task<T>> work,
        CancellationToken cancellationToken)
    {
        SolverSession? used = _solver.GetSession(url);
        try
        {
            return await WithPageAttemptAsync(url, referrer, session, work, used, cancellationToken);
        }
        catch (BotChallengeException) when (_solver.Enabled)
        {
            SolverSession solved = await _solver.SolveAsync(url, used, cancellationToken);
            try
            {
                return await WithPageAttemptAsync(url, referrer, session, work, solved, cancellationToken);
            }
            catch (BotChallengeException)
            {
                _solver.Reject(url, solved);
                throw; // one recovery only; future requests respect the solver's cooldown
            }
        }
    }

    private async Task<T> WithPageAttemptAsync<T>(string url, string? referrer, string? session, Func<IPage, Task<T>> work,
        SolverSession? solved, CancellationToken cancellationToken)
    {
        EnsureBrowserInitialized();
        if (_browser is null)
            throw new HttpRequestException($"Chromium is not available to load {url}.");

        await _pageSlots.WaitAsync(cancellationToken);
        ChromiumBrowserSession? shared = session is null ? null : GetSession(session);
        if (session is not null && shared is null)
            Log.DebugFormat("Session '{0}' is not open; loading {1} in an isolated context.", session, url);
        IBrowserContext? context = null;
        IPage? page = null;
        List<Task> failedRequestLogs = [];
        BrowserChallengeState challenge = new(new Uri(url));
        try
        {
            context = shared?.Context ?? await _browser.CreateBrowserContextAsync();
            page = await NewPageAsync(context, referrer, failedRequestLogs, solved?.UserAgent ?? shared?.UserAgent, challenge);
            if (solved is not null && solved.Cookies.Length > 0)
                await page.SetCookieAsync(solved.Cookies);

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
                    page = await NewPageAsync(context, referrer, failedRequestLogs, solved?.UserAgent ?? shared?.UserAgent, challenge);
                }
            }

            if (!success)
                throw new HttpRequestException($"Chromium request failed for {url} after retries: {lastEx?.Message}");

            await DrainResponsesAsync(failedRequestLogs);
            ThrowIfChallenged(challenge);
            T result;
            try { result = await work(page); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await DrainResponsesAsync(failedRequestLogs);
                ThrowIfChallenged(challenge);
                throw;
            }
            await DrainResponsesAsync(failedRequestLogs);
            ThrowIfChallenged(challenge);
            return result;
        }
        finally
        {
            // Let failed-request logging read its response bodies before the page they belong to goes away.
            Task[] pendingLogs;
            lock (failedRequestLogs)
                pendingLogs = failedRequestLogs.ToArray();
            await Task.WhenAll(pendingLogs).WaitAsync(TimeSpan.FromSeconds(5)).ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously);

            try
            {
                // A session's context outlives this use; only the page opened here goes.
                if (shared is not null)
                {
                    if (page is not null)
                        await page.CloseAsync();
                }
                else if (context is not null)
                {
                    await context.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Log.WarnFormat("Error closing browser page/context: {0}", ex.Message);
            }
            _pageSlots.Release();
        }
    }

    private void ThrowIfChallenged(BrowserChallengeState state)
    {
        if (_solver.Enabled && state.Challenged)
            throw new BotChallengeException(state.Origin.GetLeftPart(UriPartial.Authority));
    }

    private static async Task DrainResponsesAsync(List<Task> responses)
    {
        Task[] pending;
        lock (responses) pending = responses.ToArray();
        try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { }
    }

    private sealed class BrowserChallengeState(Uri origin)
    {
        internal Uri Origin { get; } = origin;
        internal volatile bool Challenged;
    }

    private ChromiumBrowserSession? GetSession(string name) => _sessions.GetValueOrDefault(name);

    // Diagnostic seam: seed a new session before its first navigation. Normal requests recover
    // automatically through the challenge solver. Refuse to replace a session that may have active renders.
    internal async Task ImportSessionAsync(string name, string userAgent, CookieParam[] cookies,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        EnsureBrowserInitialized();
        if (_browser is null)
            throw new HttpRequestException("Chromium is not available to import the solver session.");

        await _sessionsLock.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.ContainsKey(name))
                throw new InvalidOperationException($"Browser session '{name}' is already open.");
            IBrowserContext context = await _browser.CreateBrowserContextAsync();
            try
            {
                IPage page = await context.NewPageAsync();
                await page.SetUserAgentAsync(userAgent);
                await page.SetCookieAsync(cookies);
                _sessions[name] = new ChromiumBrowserSession(context, page) { UserAgent = userAgent };
            }
            catch
            {
                await context.CloseAsync();
                throw;
            }
        }
        finally
        {
            _sessionsLock.Release();
        }
    }

    private sealed class ChromiumBrowserSession(IBrowserContext context, IPage page)
    {
        public IBrowserContext Context => context;
        public IPage Page => page;
        public string? UserAgent { get; init; }
    }

    private static async Task<bool> WaitForSelectorAsync(IPage page, string selector)
    {
        try
        {
            await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = SelectorTimeoutMs });
            return true;
        }
        catch (WaitTaskTimeoutException)
        {
            return false;
        }
    }

    private sealed class PuppeteerBrowserPage(IPage page, CancellationToken cancellationToken) : IBrowserPage
    {
        public Task<bool> WaitForSelectorAsync(string selector) => ChromiumHeadlessBrowser.WaitForSelectorAsync(page, selector);

        public async Task<IReadOnlyList<string>> QueryAttributesAsync(string selector, string attribute) =>
            await page.EvaluateFunctionAsync<string[]>(
                "(selector, attribute) => Array.from(document.querySelectorAll(selector), e => e.getAttribute(attribute) ?? '')",
                selector, attribute);

        public async Task<bool> ScrollByAsync(string? containerSelector, int pixels, int settleMs)
        {
            bool atEnd = await page.EvaluateFunctionAsync<bool>("""
                (selector, pixels) => {
                  const el = selector ? document.querySelector(selector) : document.scrollingElement;
                  if (!el) return true;
                  el.scrollTop += pixels;
                  return el.scrollTop + el.clientHeight >= el.scrollHeight - 2;
                }
                """, containerSelector!, pixels);
            if (settleMs > 0)
                await Task.Delay(settleMs, cancellationToken);
            return atEnd;
        }
    }

    private async Task<IPage> NewPageAsync(IBrowserContext context, string? referrer, List<Task> failedRequestLogs,
        string? userAgent = null, BrowserChallengeState? challenge = null)
    {
        IPage page = await context.NewPageAsync();
        page.Response += (_, e) =>
        {
            lock (failedRequestLogs)
                failedRequestLogs.Add(InspectResponseAsync(e.Response, challenge));
        };
        await page.SetUserAgentAsync(userAgent ?? _settings.UserAgent);
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

    // A site that gates its data API still renders its page shell, so a failed background request is often the
    // only evidence of why content never appeared (e.g. Comix answering 403 captcha_required).
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

    private static async Task InspectResponseAsync(IResponse response, BrowserChallengeState? challenge)
    {
        await LogFailedDataRequestAsync(response);
        if (challenge is null || !Uri.TryCreate(response.Url, UriKind.Absolute, out Uri? uri)
            || uri.GetLeftPart(UriPartial.Authority) != challenge.Origin.GetLeftPart(UriPartial.Authority)
            || response.Request.ResourceType is not (ResourceType.Document or ResourceType.Xhr or ResourceType.Fetch))
            return;
        try
        {
            string? server = response.Headers.FirstOrDefault(h => h.Key.Equals("server", StringComparison.OrdinalIgnoreCase)).Value;
            bool mitigated = response.Headers.Any(h => h.Key.Equals("cf-mitigated", StringComparison.OrdinalIgnoreCase)
                && h.Value.Equals("challenge", StringComparison.OrdinalIgnoreCase));
            if (!mitigated && (int)response.Status is not (403 or 503))
                return;
            if (BotChallenge.IsChallenge((int)response.Status, server, mitigated, await response.TextAsync()))
                challenge.Challenged = true;
        }
        catch (Exception ex)
        {
            Log.DebugFormat("Could not inspect browser challenge response: {0}", ex.GetType().Name);
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
