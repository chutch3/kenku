using API.HttpRequesters.Interfaces;
using HtmlAgilityPack;
using log4net;
using System.Net;
using System.Text.Json;

namespace API.HttpRequesters;

// Recovers HTTP requests through the configured challenge solver.
public class ChallengeSolverRequester : IHttpRequester
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChallengeSolverRequester));
    private readonly HttpClient _client;
    private readonly ChallengeSolverClient _solver;
    private readonly CookieContainer? _cookies;

    public ChallengeSolverRequester(HttpClient client, KenkuSettings settings)
        : this(client, new ChallengeSolverClient(client, settings), null) { }

    internal ChallengeSolverRequester(HttpClient client, ChallengeSolverClient solver, CookieContainer? cookies)
    {
        _client = client;
        _solver = solver;
        _cookies = cookies;
    }

    public Task<HttpResponseMessage> MakeRequest(string url, RequestType requestType, string? referrer = null,
        CancellationToken? cancellationToken = null) =>
        RecoverAsync(url, requestType, referrer, null, cancellationToken ?? CancellationToken.None);

    internal async Task<HttpResponseMessage> RecoverAsync(string url, RequestType requestType, string? referrer,
        SolverSession? failedSession, CancellationToken ct)
    {
        try
        {
            SolverSession solved = await _solver.SolveAsync(url, failedSession, ct);
            // Only use a rendered body for the URL it actually belongs to. Concurrent requests for
            // another page share clearance, never another page's HTML. Assets always need real bytes.
            if (requestType is not (RequestType.SeriesImage or RequestType.SeriesCover or RequestType.Archive)
                && new Uri(solved.RequestedUrl) == new Uri(url))
            {
                return new(HttpStatusCode.OK) { Content = new StringContent(UnwrapJson(solved.Html)) };
            }

            using HttpRequestMessage request = new(HttpMethod.Get, url);
            solved.Apply(request, _cookies);
            if (referrer is not null)
                request.Headers.Referrer = new Uri(referrer);
            HttpResponseMessage response = await _client.SendAsync(request,
                requestType == RequestType.Archive ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, ct);
            if (await BotChallenge.IsChallengeAsync(response, ct))
            {
                Log.WarnFormat("{0} still challenged the solver's session; discarding it.", new Uri(url).Host);
                _solver.Reject(url, solved);
                solved.Forget(_cookies);
            }
            return response; // one retry only, including for binary assets
        }
        catch (HttpRequestException ex)
        {
            Log.Warn(ex.Message);
            return new(HttpStatusCode.ServiceUnavailable);
        }
    }

    private static string UnwrapJson(string html)
    {
        HtmlDocument document = new();
        document.LoadHtml(html);
        string? pre = document.DocumentNode.SelectSingleNode("//pre")?.InnerText;
        if (pre is null)
            return html;
        string decoded = HtmlEntity.DeEntitize(pre);
        try
        {
            using JsonDocument json = JsonDocument.Parse(decoded);
            return decoded;
        }
        catch (JsonException) { return html; }
    }
}
