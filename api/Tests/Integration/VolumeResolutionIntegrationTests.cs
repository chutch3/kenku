using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The Dandadan fix end-to-end through the booted app: the real search service, MangaDex resolver,
/// Wikipedia resolver, scoring and merger run via the real <c>POST /v2/Maintenance/ResolveMissingVolumes</c>
/// endpoint + dispatcher; only the HTTP server is simulated (WireMock, with the captured real bodies in
/// Fixtures/Dandadan), so faults (500s, malformed bodies) can prove resilience.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeResolutionIntegrationTests : OutboundHttpIntegrationTest
{
    private const string DandadanId = "68112dc1-2b80-4f20-beb8-2f2a8716a430";

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    private void StubSearch(IResponseBuilder resp) =>
        Server.Given(Request.Create().WithPath("/manga").WithParam("title").UsingGet()).RespondWith(resp);

    private void StubAggregate(IResponseBuilder resp) =>
        Server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet()).RespondWith(resp);

    private void StubWikipedia(IResponseBuilder resp) =>
        Server.Given(Request.Create().WithPath("/w/api.php").UsingGet()).RespondWith(resp);

    private static IResponseBuilder Ok(string body) => Response.Create().WithStatusCode(200).WithBody(body);

    private void StubMangaDexOk()
    {
        StubSearch(Ok(Fixture("search.json")));
        StubAggregate(Ok(Fixture("aggregate-all.json")));
    }

    private Task<string> SeedDandadan(Action<Series, SeriesContext> addChapters) => App.WithSeriesContext(async ctx =>
    {
        var library = new FileLibrary(Path.Combine(Path.GetTempPath(), "kenku-it-" + Guid.NewGuid().ToString("N")), "Lib");
        ctx.FileLibraries.Add(library);
        var manga = new Series("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        ctx.Series.Add(manga);
        addChapters(manga, ctx);
        await ctx.SaveChangesAsync();
        return manga.Key;
    });

    private static void AddChapter(Series manga, SeriesContext ctx, string number, int? volume = null, MetadataConfidence? confidence = null) =>
        ctx.Chapters.Add(new Chapter(manga, number, volume, null)
            { Downloaded = true, FileName = $"Ch.{number}.cbz", MetadataConfidence = confidence });

    private async Task Resolve()
    {
        (await App.CreateClient().PostAsync("/v2/Maintenance/ResolveMissingVolumes", null)).EnsureSuccessStatusCode();
        await DrainJobsAsync();
    }

    private Task<Dictionary<string, Chapter>> ChaptersByNumber() =>
        App.WithSeriesContext(c => c.Chapters.ToDictionaryAsync(x => x.ChapterNumber, x => x));

    [Fact]
    public async Task ResolvesVolumesFromAllSources_HonorsManualAssignments_AndLeavesUncollectedChaptersLoose()
    {
        StubMangaDexOk();
        StubWikipedia(Ok(Fixture("wikitext.json")));

        string key = await SeedDandadan((m, ctx) =>
        {
            AddChapter(m, ctx, "1");                                          // MangaDex+Wiki → 1
            AddChapter(m, ctx, "50", 1, MetadataConfidence.Manual);          // manual (real vol 7) → must hold
            AddChapter(m, ctx, "85");                                         // MangaDex last → 10
            AddChapter(m, ctx, "86");                                         // Wikipedia only → 11
            AddChapter(m, ctx, "148", 17, MetadataConfidence.Heuristic);     // STALE → Wikipedia heals to 18
            AddChapter(m, ctx, "201");                                        // Wikipedia → 23
            AddChapter(m, ctx, "235");                                        // past last volume → stays loose
        });

        await Resolve();

        var ch = await ChaptersByNumber();
        var source = await App.WithSeriesContext(c => c.Set<MetadataSource>().FirstAsync(s => s.MangaId == key));
        Assert.Equal(MetadataSourceStatus.AutoMatched, source.Status);   // scoring fix: matched despite empty lastChapter
        Assert.Equal(DandadanId, source.ExternalId);
        Assert.Equal(1, ch["1"].VolumeNumber);
        Assert.Equal(10, ch["85"].VolumeNumber);
        Assert.Equal(11, ch["86"].VolumeNumber);                          // Wikipedia fills the MangaDex gap
        Assert.Equal(18, ch["148"].VolumeNumber);                         // stale heuristic healed
        Assert.Equal(23, ch["201"].VolumeNumber);
        Assert.Equal(1, ch["50"].VolumeNumber);                           // manual lock held
        Assert.Equal(MetadataConfidence.Manual, ch["50"].MetadataConfidence);
        Assert.Null(ch["235"].VolumeNumber);                             // uncollected → loose
    }

    [Fact]
    public async Task WhenAMetadataSourceIsUnavailable_ResolvesFromTheRemainingSources()
    {
        StubSearch(Ok(Fixture("search.json")));
        StubAggregate(Response.Create().WithStatusCode(500)); // MangaDex aggregate is down
        StubWikipedia(Ok(Fixture("wikitext.json")));

        await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "86"); AddChapter(m, ctx, "201"); });

        await Resolve();

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);    // Wikipedia covers these
        Assert.Equal(11, ch["86"].VolumeNumber);
        Assert.Equal(23, ch["201"].VolumeNumber);
    }

    [Fact]
    public async Task WhenAMetadataSourceReturnsInvalidData_StaysHealthy_AndResolvesFromTheRemainingSources()
    {
        // MangaDexVolumeResolver has no try/catch around JObject.Parse; the pipeline must absorb the throw
        // and still resolve via the other source.
        StubSearch(Ok(Fixture("search.json")));
        StubAggregate(Response.Create().WithStatusCode(200).WithBody("{ this is not valid json"));
        StubWikipedia(Ok(Fixture("wikitext.json")));

        await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "86"); });

        await Resolve();

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);
        Assert.Equal(11, ch["86"].VolumeNumber);
    }

    [Fact]
    public async Task WhenADifferentSourceIsUnavailable_ResolvesWhatTheOthersCover_RestStaysLoose()
    {
        StubMangaDexOk();
        StubWikipedia(Response.Create().WithStatusCode(500)); // Wikipedia is down

        await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "85"); AddChapter(m, ctx, "86"); AddChapter(m, ctx, "201"); });

        await Resolve();

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);    // MangaDex still covers 1–85
        Assert.Equal(10, ch["85"].VolumeNumber);
        Assert.Null(ch["86"].VolumeNumber);       // only Wikipedia had these → loose
        Assert.Null(ch["201"].VolumeNumber);
    }
}
