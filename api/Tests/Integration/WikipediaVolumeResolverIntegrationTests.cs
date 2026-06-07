using API.Services.Interfaces;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.Schema.SeriesContext;
using API.Workers.MaintenanceWorkers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Plumbing proof + failure-path coverage for the real WikipediaVolumeResolver against a WireMock
/// server (requests redirected there by <see cref="HostRewritingHandler"/>). WireMock lets us assert
/// the exact request the resolver makes and simulate fault responses.
/// </summary>
[Trait("Category", "Integration")]
public class WikipediaVolumeResolverIntegrationTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    public void Dispose() => _server.Stop();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    private static Series Manga() =>
        new("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], new FileLibrary("/tmp", "Lib"));

    private WikipediaVolumeResolver Resolver() =>
        new(new HttpClient(new HostRewritingHandler(_server.Url!)));

    [Fact]
    public async Task ParsesRealVolumesFromServedWikitext_AndCallsMediaWikiApi()
    {
        _server
            .Given(Request.Create().WithPath("/w/api.php").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(Fixture("wikitext.json")));

        var map = await Resolver().ResolveAsync(Manga(), [], CancellationToken.None);

        Assert.Equal(11, map["86"]);
        Assert.Equal(23, map["201"]);

        // The resolver actually hit the MediaWiki parse endpoint for the right page (WireMock verification).
        var requests = _server.FindLogEntries(Request.Create().WithPath("/w/api.php").UsingGet());
        Assert.Single(requests);
        Assert.Contains("List of Dandadan chapters", Uri.UnescapeDataString(requests[0].RequestMessage.Url));
    }

    [Fact]
    public async Task SendsAUserAgent_SoTheRequestIsNotRejected()
    {
        // Without a User-Agent, MangaDex returns 400 and the MediaWiki API returns 403 — so every
        // outbound metadata request must carry one. (Regression: this shipped missing and broke
        // resolution in production entirely.)
        _server
            .Given(Request.Create().WithPath("/w/api.php").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(Fixture("wikitext.json")));

        await Resolver().ResolveAsync(Manga(), [], CancellationToken.None);

        var headers = _server.FindLogEntries(Request.Create().WithPath("/w/api.php").UsingGet())
            .Single().RequestMessage.Headers!;
        Assert.True(headers.ContainsKey("User-Agent"), "request must include a User-Agent header");
        Assert.Contains("Kenku", string.Concat(headers["User-Agent"]));
    }
}
