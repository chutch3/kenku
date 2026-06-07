using API.HttpRequesters.Interfaces;
using System.Net;
using log4net;

namespace API.HttpRequesters;

internal class HttpRequester : IHttpRequester
{
    private readonly HttpClient _client;
    private readonly FlareSolverrRequester _flareSolverrClient;
    private ILog Log { get; } = LogManager.GetLogger(typeof(HttpRequester));

    public HttpRequester(RateLimitHandler rateLimitHandler, KenkuSettings settings)
    {
        // The request timeout lives in RateLimitHandler so it covers only the network send, not the
        // rate-limit queue wait. HttpClient.Timeout would wrap the whole chain (queue wait included),
        // which is the #31 download loop, so it is disabled here.
        _client = new HttpClient(handler: rateLimitHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
            DefaultRequestHeaders = { { "User-Agent", settings.UserAgent } }
        };
        _flareSolverrClient = new FlareSolverrRequester(_client, settings);
    }

    public async Task<HttpResponseMessage> MakeRequest(string url, RequestType requestType, string? referrer = null, CancellationToken? cancellationToken = null)
    {
        Log.DebugFormat("Using {0} for {1}", typeof(HttpRequester).FullName, url);
        HttpRequestMessage requestMessage = new(HttpMethod.Get, url);
        if (referrer is not null)
            requestMessage.Headers.Referrer = new(referrer);
        Log.DebugFormat("Requesting {0}", url);

        try
        {
            HttpResponseMessage response = await _client.SendAsync(requestMessage, cancellationToken ?? CancellationToken.None);
            Log.DebugFormat("Request {0} returned {1} {2}", url, (int)response.StatusCode, response.StatusCode.ToString());
            if(response.IsSuccessStatusCode)
                return response;

            if (response.Headers.Server.Any(s =>
                    (s.Product?.Name ?? "").Contains("cloudflare", StringComparison.InvariantCultureIgnoreCase)))
            {
                Log.Debug("Retrying with FlareSolverr!");
                return await _flareSolverrClient.MakeRequest(url, requestType, referrer);
            }

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
            return new(HttpStatusCode.InternalServerError);
        }
        catch (HttpRequestException e)
        {
            Log.Error(e);
            return new(HttpStatusCode.InternalServerError);
        }
    }
}
