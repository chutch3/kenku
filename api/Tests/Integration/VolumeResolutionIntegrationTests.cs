using System.Collections.Concurrent;
using API;
using API.Schema.SeriesContext;
using API.Services;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The Dandadan fix end-to-end over WireMock: the REAL search service, MangaDex resolver, Wikipedia
/// resolver, scoring and merger run through the worker; only the HTTP server is simulated (responses are
/// the captured real bodies in Fixtures/Dandadan). Unlike the hand-rolled handler, WireMock matches exact
/// requests, verifies them, and lets us simulate faults (500s, malformed bodies) to prove resilience.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeResolutionIntegrationTests : IDisposable
{
    private const string DandadanId = "68112dc1-2b80-4f20-beb8-2f2a8716a430";
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly IntegrationHarness _harness = new();

    public VolumeResolutionIntegrationTests() =>
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess;

    public void Dispose() { _server.Stop(); _harness.Dispose(); }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    private static IResponseBuilder Ok(string body) => Response.Create().WithStatusCode(200).WithBody(body);

    private void StubSearch(IResponseBuilder resp) =>
        _server.Given(Request.Create().WithPath("/manga").WithParam("title").UsingGet()).RespondWith(resp);

    private void StubAggregate(IResponseBuilder resp) =>
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet()).RespondWith(resp);

    private void StubWikipedia(IResponseBuilder resp) =>
        _server.Given(Request.Create().WithPath("/w/api.php").UsingGet()).RespondWith(resp);

    // The en-first MangaDex merge yields the same vols 1–10 whether or not we split en/all (that nuance is
    // unit-tested in MangaDexVolumeResolverTests), so one aggregate stub keeps the integration test focused.
    private void StubMangaDexOk()
    {
        StubSearch(Ok(Fixture("search.json")));
        StubAggregate(Ok(Fixture("aggregate-all.json")));
    }

    private async Task ResolveAsync(string mangaKey)
    {
        var http = new HttpClient(new HostRewritingHandler(_server.Url!));
        var service = new VolumeResolutionService(_harness.Settings,
            new MangaDexVolumeResolver(http), new MangaDexSearchService(http),
            new IVolumeResolver[] { new WikipediaVolumeResolver(http) });
        using var scope = _harness.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SeriesContext>();
        await service.ResolveAsync(ctx, mangaKey, CancellationToken.None);
    }

    private async Task<string> SeedDandadan(Action<Series, SeriesContext> addChapters)
    {
        string key = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
            ctx.Series.Add(manga);
            addChapters(manga, ctx);
            key = manga.Key;
            return Task.CompletedTask;
        });
        return key;
    }

    private static void AddChapter(Series manga, SeriesContext ctx, string number, int? volume = null, MetadataConfidence? confidence = null) =>
        ctx.Chapters.Add(new Chapter(manga, number, volume, null)
            { Downloaded = true, FileName = $"Ch.{number}.cbz", MetadataConfidence = confidence });

    private Task<Dictionary<string, Chapter>> ChaptersByNumber() =>
        _harness.Query(ctx => ctx.Chapters.ToDictionaryAsync(c => c.ChapterNumber, c => c));

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

        await ResolveAsync(key);

        var ch = await ChaptersByNumber();
        var source = await _harness.Query(c => c.Set<MetadataSource>().FirstAsync(s => s.MangaId == key));
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

        string key = await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "86"); AddChapter(m, ctx, "201"); });

        await ResolveAsync(key);

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);    // Wikipedia covers these
        Assert.Equal(11, ch["86"].VolumeNumber);
        Assert.Equal(23, ch["201"].VolumeNumber);
    }

    [Fact]
    public async Task WhenAMetadataSourceReturnsInvalidData_StaysHealthy_AndResolvesFromTheRemainingSources()
    {
        // MangaDexVolumeResolver has no try/catch around JObject.Parse; the worker must absorb the throw
        // and still resolve via the other source.
        StubSearch(Ok(Fixture("search.json")));
        StubAggregate(Response.Create().WithStatusCode(200).WithBody("{ this is not valid json"));
        StubWikipedia(Ok(Fixture("wikitext.json")));

        string key = await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "86"); });

        var ex = await Record.ExceptionAsync(() => ResolveAsync(key));
        Assert.Null(ex); // pipeline did not crash

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);
        Assert.Equal(11, ch["86"].VolumeNumber);
    }

    [Fact]
    public async Task WhenADifferentSourceIsUnavailable_ResolvesWhatTheOthersCover_RestStaysLoose()
    {
        StubMangaDexOk();
        StubWikipedia(Response.Create().WithStatusCode(500)); // Wikipedia is down

        string key = await SeedDandadan((m, ctx) => { AddChapter(m, ctx, "1"); AddChapter(m, ctx, "85"); AddChapter(m, ctx, "86"); AddChapter(m, ctx, "201"); });

        await ResolveAsync(key);

        var ch = await ChaptersByNumber();
        Assert.Equal(1, ch["1"].VolumeNumber);    // MangaDex still covers 1–85
        Assert.Equal(10, ch["85"].VolumeNumber);
        Assert.Null(ch["86"].VolumeNumber);       // only Wikipedia had these → loose
        Assert.Null(ch["201"].VolumeNumber);
    }
}
