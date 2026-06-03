namespace API.Tests.Integration;

/// <summary>
/// Test-only message handler that redirects requests aimed at hardcoded upstream hosts (e.g.
/// https://api.mangadex.org, https://en.wikipedia.org) to a local WireMock server, preserving the path
/// and query string. This lets us point production code that builds absolute URLs at WireMock without
/// changing that code.
/// </summary>
public sealed class HostRewritingHandler(string targetBaseUrl) : DelegatingHandler(new HttpClientHandler())
{
    private readonly Uri _target = new(targetBaseUrl);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var original = request.RequestUri!;
        request.RequestUri = new UriBuilder(original)
        {
            Scheme = _target.Scheme,
            Host = _target.Host,
            Port = _target.Port,
        }.Uri;
        return base.SendAsync(request, cancellationToken);
    }
}
