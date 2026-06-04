using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Outside-in: the real app resolves volumes through the HTTP endpoint. The MangaDex search (stubbed via
/// WireMock) returns a decoy that wins on title + chapter-count and the true entry that only matches by
/// AniList id. Identifier matching must link the series to the true entry, not the higher-scoring decoy.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeIdentifierMatchEndToEndTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;

    public VolumeIdentifierMatchEndToEndTests() => _app = new KenkuApplicationFactory { OutboundHttpTarget = _server.Url! };

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
    public async Task Resolve_LinksSeriesToTheMangaDexEntryMatchingItsAniListId_OverAHigherScoringDecoy()
    {
        // decoy-uuid wins title (exact) + count; true-uuid only matches because its links.al == our 87170.
        const string searchJson = """
        {"result":"ok","response":"collection","data":[
          {"id":"decoy-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch"},"lastChapter":"1","links":{"al":"999"}},"relationships":[]},
          {"id":"true-uuid","type":"manga","attributes":{"title":{"en":"Fire Punch (Remaster)"},"lastChapter":"83","links":{"al":"87170"}},"relationships":[]}
        ]}
        """;
        const string aggregateJson = """
        {"result":"ok","volumes":{"1":{"volume":"1","chapters":{"1":{"chapter":"1"}}}}}
        """;

        _server.Given(Request.Create().WithPath("/manga").WithParam("title").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(searchJson));
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(aggregateJson));
        _server.Given(Request.Create().WithPath("/w/api.php").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"parse":{"wikitext":{"*":""}}}"""));

        string mangaKey = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-id-" + Guid.NewGuid().ToString("N")), "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Fire Punch", "d", "u", SeriesReleaseStatus.Continuing, [], [],
                [new Link("AniList", "https://anilist.co/manga/87170")], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
            ctx.Series.Add(manga);
            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = "Ch.1.cbz" });
            await ctx.SaveChangesAsync();
            return manga.Key;
        });

        var response = await _app.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null);
        response.EnsureSuccessStatusCode();

        // Wait until the series gets linked, then assert it linked to the id-matched entry (not the decoy).
        bool linked = await WaitUntil(async () =>
            !string.IsNullOrEmpty((await _app.WithSeriesContext(c =>
                c.Set<MetadataSource>().FirstAsync(s => s.MangaId == mangaKey))).ExternalId),
            TimeSpan.FromSeconds(30));
        Assert.True(linked, "series was never linked");

        var source = await _app.WithSeriesContext(c => c.Set<MetadataSource>().FirstAsync(s => s.MangaId == mangaKey));
        Assert.Equal("true-uuid", source.ExternalId);
    }
}
