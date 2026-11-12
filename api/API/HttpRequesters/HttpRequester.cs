using API.HttpRequesters.Interfaces;
using API.Services;
using System.Net;
using log4net;

namespace API.HttpRequesters;

internal class HttpRequester : IHttpRequester
{
    private readonly HttpClient _client;
    private readonly ChallengeSolverRequester _challengeSolverRequester;
    private readonly ChallengeSolverClient _solver;
    private readonly CookieContainer? _cookies;
    private ILog Log { get; } = LogManager.GetLogger(typeof(HttpRequester));

    public HttpRequester(RateLimitHandler rateLimitHandler, KenkuSettings settings, ChallengeSolverClient solver)
    {
        // The request timeout lives in RateLimitHandler so it covers only the network send, not the
        // rate-limit queue wait. HttpClient.Timeout would wrap the whole chain (queue wait included),
        // which is the #31 download loop, so it is disabled here.
        _client = new HttpClient(handler: rateLimitHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        _client.SetUserAgent(settings.UserAgent);
        _solver = solver;
        _cookies = rateLimitHandler.CookieJar;
        _challengeSolverRequester = new ChallengeSolverRequester(_client, _solver, _cookies);
    }

    public async Task<HttpResponseMessage> MakeRequest(string url, RequestType requestType, string? referrer = null, CancellationToken? cancellationToken = null)
    {
        Log.DebugFormat("Using {0} for {1}", typeof(HttpRequester).FullName, url);
        using HttpRequestMessage requestMessage = new(HttpMethod.Get, url);
        SolverSession? usedSession = _solver.GetSession(url);
        usedSession?.Apply(requestMessage, _cookies);
        if (referrer is not null)
            requestMessage.Headers.Referrer = new(referrer);
        Log.DebugFormat("Requesting {0}", url);

        try
        {
            HttpResponseMessage response = await _client.SendAsync(requestMessage,
                requestType == RequestType.Archive ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead,
                cancellationToken ?? CancellationToken.None);
            Log.DebugFormat("Request {0} returned {1} {2}", url, (int)response.StatusCode, response.StatusCode.ToString());
            if (_solver.Enabled && await BotChallenge.IsChallengeAsync(response, cancellationToken ?? CancellationToken.None))
            {
                Log.InfoFormat("Detected a challenge from {0}; recovering through the challenge solver.", new Uri(url).Host);
                response.Dispose();
                return await _challengeSolverRequester.RecoverAsync(url, requestType, referrer, usedSession,
                    cancellationToken ?? CancellationToken.None);
            }
            if (response.IsSuccessStatusCode)
                return response;

            // Only read bodies (async, never .Result) when debug logging is actually enabled. Blocking on
            // .Result here risked thread-pool starvation / sync-over-async deadlocks on every failed request.
            if (Log.IsDebugEnabled)
            {
                CancellationToken ct = cancellationToken ?? CancellationToken.None;
                string requestBody = requestMessage.Content is null ? "" : await requestMessage.Content.ReadAsStringAsync(ct);
                string responseBody = await response.Content.ReadAsStringAsync(ct);
                Log.Debug($"Request returned status code {(int)response.StatusCode} {response.StatusCode}:\n" +
                          $"=====\n" +
                          $"Request:\n" +
                          $"{requestMessage.Method} {requestMessage.RequestUri}\n" +
                          $"{requestMessage.Version} {requestMessage.VersionPolicy}\n" +
                          $"Headers:\n\t{string.Join("\n\t", requestMessage.Headers.Select(h => $"{h.Key}: <{string.Join(">, <", h.Value)}"))}>\n" +
                          $"{requestBody}" +
                          $"=====\n" +
                          $"Response:\n" +
                          $"{response.Version}\n" +
                          $"Headers:\n\t{string.Join("\n\t", response.Headers.Select(h => $"{h.Key}: <{string.Join(">, <", h.Value)}"))}>\n" +
                          $"{responseBody}");
            }
            HttpStatusCode status = response.StatusCode;
            response.Dispose();
            return new(status);
        }
        catch (HttpRequestException e)
        {
            Log.Error(e);
            return new(HttpStatusCode.InternalServerError);
        }
    }
}
