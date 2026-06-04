using System.Net;
using System.Text;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Moq;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in coverage of the whole feature as one chain: import a series from a connector (which captures
/// its AniList link), then resolve volumes — the captured link must drive an identifier match to the right
/// MangaDex entry over a higher-scoring decoy. Only the two HTTP edges are stubbed (connector + MangaDex);
/// the link is never preset, so this exercises the seam between capture and matching.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeMatchFromImportEndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;

    public VolumeMatchFromImportEndToEndTests()
    {
        const string seriesPageHtml = """
            <html><head><title>Fire Punch | Weeb Central</title></head>
            <body><a href="https://anilist.co/manga/87170/Fire-Punch">AniList</a></body></html>
            """;
        var requester = new Mock<IHttpRequester>();
        requester
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(seriesPageHtml, Encoding.UTF8, "text/html")
            });

        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ConnectorHttpRequester = requester.Object
        };
    }

    public void Dispose() { _app.Dispose(); _server.Stop(); }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }
        return false;
    }

    [Fact]
    public async Task ImportedSeries_MatchesMangaDexByItsCapturedAniListLink_OverAHigherScoringDecoy()
    {
        const string searchJson = """
        {"result":"ok","response":"collection","data":[
          {"id":"decoy-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch"},"lastChapter":"1","links":{"al":"999"}},"relationships":[]},
          {"id":"true-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch (Remaster)"},"lastChapter":"83","links":{"al":"87170"}},"relationships":[]}
        ]}
        """;
        _server.Given(Request.Create().WithPath("/manga").WithParam("title").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(searchJson));
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBody("""{"result":"ok","volumes":{"1":{"volume":"1","chapters":{"1":{"chapter":"1"}}}}}"""));
        _server.Given(Request.Create().WithPath("/w/api.php").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"parse":{"wikitext":{"*":""}}}"""));

        string libraryKey = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-imp-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            await ctx.SaveChangesAsync();
            return library.Key;
        });

        // 1. Import the series from the connector — this is where the AniList link is captured.
        (await _app.CreateClient().PostAsync(
            $"/v2/Series/unknown/ChangeLibrary/{libraryKey}?connectorName=WeebCentral&connectorSeriesId=wc-1", null))
            .EnsureSuccessStatusCode();

        // 2. Give it a downloaded chapter so there is something to resolve.
        string mangaKey = await _app.WithSeriesContext(async ctx =>
        {
            var manga = await ctx.Series.FirstAsync(m => m.Name == "Fire Punch");
            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = "Ch.1.cbz" });
            await ctx.SaveChangesAsync();
            return manga.Key;
        });

        // 3. Resolve — the captured link must pick true-uuid even though the decoy wins on title + count.
        (await _app.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null)).EnsureSuccessStatusCode();

        bool matched = await WaitUntil(async () =>
            (await _app.WithSeriesContext(c => c.Set<MetadataSource>().FirstAsync(s => s.MangaId == mangaKey))).ExternalId == "true-uuid",
            TimeSpan.FromSeconds(30));
        Assert.True(matched, "imported series did not match MangaDex via its captured AniList link");
    }
}
