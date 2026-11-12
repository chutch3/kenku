using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using log4net;
using Newtonsoft.Json.Linq;
using PuppeteerSharp;

namespace API.HttpRequesters;

/// <summary>Shared, bounded solver access for HTTP and Chromium. Sessions are scoped to an origin;
/// concurrent failures share a solve, and a failed solve cannot trigger a retry storm.
/// Uses the request.get /v1 protocol supported by Byparr; not a universal solver protocol.</summary>
public sealed class ChallengeSolverClient : IDisposable
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChallengeSolverClient));
    private readonly HttpClient _client;
    private readonly KenkuSettings _settings;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _cooldown;
    private readonly bool _ownsClient;
    private readonly SemaphoreSlim _solveSlots = new(2, 2);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    private sealed class Entry
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal SolverSession? Session;
        internal DateTimeOffset LastAttempt = DateTimeOffset.MinValue;
    }

    public ChallengeSolverClient(KenkuSettings settings)
        : this(new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan }, settings)
    {
        _ownsClient = true;
    }

    internal ChallengeSolverClient(HttpClient client, KenkuSettings settings, TimeProvider? clock = null, TimeSpan? cooldown = null)
    {
        _client = client;
        _settings = settings;
        _clock = clock ?? TimeProvider.System;
        _cooldown = cooldown ?? TimeSpan.FromMinutes(5);
    }

    internal bool Enabled => !string.IsNullOrWhiteSpace(_settings.ChallengeSolverUrl);
    private string Key(string url) => _settings.ChallengeSolverUrl + "|" + new Uri(url).GetLeftPart(UriPartial.Authority);

    internal SolverSession? GetSession(string url)
    {
        if (!Enabled || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            return null;
        SolverSession? session = _entries.GetValueOrDefault(Key(url))?.Session;
        return session is not null && session.Expires > _clock.GetUtcNow() ? session : null;
    }

    internal void Reject(string url, SolverSession session)
    {
        if (_entries.TryGetValue(Key(url), out Entry? entry))
            Interlocked.CompareExchange(ref entry.Session, null, session);
    }

    internal async Task<SolverSession> SolveAsync(string url, SolverSession? failedSession, CancellationToken ct)
    {
        Uri target = SolvableUrl(url);
        Entry entry = _entries.GetOrAdd(Key(url), _ => new Entry());
        await entry.Gate.WaitAsync(ct);
        try
        {
            SolverSession? cached = GetSession(url);
            if (cached is not null && !ReferenceEquals(cached, failedSession))
                return cached;
            if (_clock.GetUtcNow() - entry.LastAttempt < _cooldown)
                throw new HttpRequestException($"Challenge solver recovery for {target.Host} is cooling down after a recent attempt.");

            entry.LastAttempt = _clock.GetUtcNow();
            entry.Session = null;
            SolverSession solved = await RequestSolutionAsync(target, url, ct);
            entry.Session = solved;
            Log.InfoFormat("Challenge solver returned a session for {0}.", target.Host);
            return solved;
        }
        finally { entry.Gate.Release(); }
    }

    /// <summary>Asks the solver to solve <paramref name="url"/> without reusing or caching a session or applying the
    /// cooldown: a connection test, which must reach the solver every time.</summary>
    internal Task CheckAsync(string url, CancellationToken ct) => RequestSolutionAsync(SolvableUrl(url), url, ct);

    private Uri SolvableUrl(string url)
    {
        if (!Enabled)
            throw new HttpRequestException("Challenge solver is not configured.");
        Uri target = new(url);
        if (target.Scheme is not ("http" or "https"))
            throw new HttpRequestException("Challenge solver supports HTTP(S) URLs only.");
        return target;
    }

    private async Task<SolverSession> RequestSolutionAsync(Uri target, string url, CancellationToken ct)
    {
        await _solveSlots.WaitAsync(ct);
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(75));
            Uri endpoint = new(_settings.ChallengeSolverUrl.TrimEnd('/') +
                (_settings.ChallengeSolverUrl.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "" : "/v1"));
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { cmd = "request.get", url, maxTimeout = 60000 })
            };
            if (_settings.ChallengeSolverProxyUrl is { Length: > 0 } proxy)
                request.Headers.Add("X-Proxy-Server", proxy);
            Log.InfoFormat("Asking the challenge solver to solve {0}.", target.GetLeftPart(UriPartial.Authority));
            using HttpResponseMessage response = await _client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Challenge solver returned HTTP {(int)response.StatusCode} for {target.Host}.");
            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (result.Value<string>("status") != "ok" || result["solution"] is not JObject solution
                || solution.Value<int?>("status") is not (>= 200 and < 300))
                throw new HttpRequestException($"Challenge solver could not solve the request for {target.Host}.");
            string ua = solution.Value<string>("userAgent") ?? "";
            if (string.IsNullOrWhiteSpace(ua) || ua.Contains('\r') || ua.Contains('\n'))
                throw new HttpRequestException("Challenge solver returned an invalid user agent.");
            CookieParam[] cookies = solution["cookies"]?.ToObject<CookieParam[]>() ?? [];
            foreach (CookieParam cookie in cookies)
            {
                string domain = cookie.Domain?.TrimStart('.') ?? "";
                if (domain.Length == 0 || !string.IsNullOrEmpty(cookie.Url)
                    || !(target.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                         || target.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
                    throw new HttpRequestException("Challenge solver returned a cookie outside the requested host's scope.");
            }
            string html = solution.Value<string>("response") ?? "";
            if (html.Contains("<title>Just a moment", StringComparison.OrdinalIgnoreCase)
                || html.Contains("id=\"challenge-form\"", StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException($"Challenge solver returned a challenge page for {target.Host}.");
            DateTimeOffset expires = _clock.GetUtcNow().AddMinutes(30);
            foreach (CookieParam cookie in cookies.Where(c => c.Expires > 0))
            {
                DateTimeOffset cookieExpiry = DateTimeOffset.FromUnixTimeSeconds((long)cookie.Expires!.Value);
                if (cookieExpiry < expires)
                    expires = cookieExpiry;
            }
            SolverSession solved = new(url, ua, cookies, html, expires);
            _ = solved.CookieJar(); // validate before anything publishes it
            return solved;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException($"Challenge solver timed out for {target.Host}.");
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or CookieException or FormatException or ArgumentException or InvalidCastException)
        {
            throw new HttpRequestException($"Challenge solver returned an invalid solution for {target.Host}.");
        }
        finally { _solveSlots.Release(); }
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}

internal sealed record SolverSession(string RequestedUrl, string UserAgent, CookieParam[] Cookies, string Html,
    DateTimeOffset Expires)
{
    internal CookieContainer CookieJar()
    {
        CookieContainer jar = new();
        foreach (CookieParam source in Cookies)
        {
            Cookie cookie = new(source.Name, source.Value, source.Path ?? "/", source.Domain)
            {
                Secure = source.Secure ?? false,
                HttpOnly = source.HttpOnly ?? false
            };
            if (source.Expires > 0)
                cookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)source.Expires.Value).UtcDateTime;
            jar.Add(cookie);
        }
        return jar;
    }

    /// <summary>Expires this session's cookies out of the transport jar, so a rejected clearance stops being sent.</summary>
    internal void Forget(CookieContainer? transportCookies)
    {
        if (transportCookies is null)
            return;
        foreach (Cookie cookie in CookieJar().GetAllCookies())
            transportCookies.Add(new Cookie(cookie.Name, "", cookie.Path, cookie.Domain) { Expired = true });
    }

    internal void Apply(HttpRequestMessage request, CookieContainer? transportCookies = null)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        CookieContainer cookies = CookieJar();
        if (transportCookies is not null)
            transportCookies.Add(cookies.GetAllCookies());
        else // injected test handlers do not have an HTTP transport cookie jar
        {
            string header = cookies.GetCookieHeader(request.RequestUri!);
            if (header.Length > 0)
                request.Headers.TryAddWithoutValidation("Cookie", header);
        }
    }
}
