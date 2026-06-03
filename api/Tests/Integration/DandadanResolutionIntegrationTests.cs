using System.Collections.Concurrent;
using System.Net;
using API;
using API.Schema.SeriesContext;
using API.Services;
using API.Tests;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The real fix, end-to-end. This runs the REAL search service, MangaDex resolver, Wikipedia resolver,
/// auto-match scoring and merger together through the worker — mocking only the HTTP boundary (VCR-style)
/// with responses captured verbatim from the live MangaDex and Wikipedia APIs (see Fixtures/Dandadan).
/// Nothing about the resolution units is stubbed, so this catches port bugs, real-markup parsing, the
/// ja-ro title quirk, empty lastChapter, the sparse English aggregate, and chapter-number alignment.
/// </summary>
[Trait("Category", "Integration")]
public class DandadanResolutionIntegrationTests : IDisposable
{
    private const string DandadanId = "68112dc1-2b80-4f20-beb8-2f2a8716a430";
    private readonly IntegrationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Dandadan", name));

    // Routes each real unit's HTTP call to the captured response for that endpoint.
    private static HttpResponseMessage Route(HttpRequestMessage req)
    {
        string url = req.RequestUri!.ToString();
        string? body = url switch
        {
            _ when url.Contains("wikipedia.org") => Fixture("wikitext.json"),
            _ when url.Contains("/aggregate") && url.Contains("translatedLanguage") => Fixture("aggregate-en.json"),
            _ when url.Contains("/aggregate") => Fixture("aggregate-all.json"),
            _ when url.Contains("/manga?title=") => Fixture("search.json"),
            _ => null,
        };
        return body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
    }

    private static Chapter Seed(Series manga, string number, int? volume, MetadataConfidence? confidence)
    {
        var ch = new Chapter(manga, number, volume, null)
            { Downloaded = true, FileName = $"Dandadan - Ch.{number}.cbz", MetadataConfidence = confidence };
        return ch;
    }

    [Fact]
    public async Task UnlinkedDandadan_ResolvesRealVolumes_FillsMangaDexGapFromWikipedia_AndHealsStaleHeuristic()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess;

        string mangaKey = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Dandadan", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked; // start cold — auto-match must run
            ctx.Series.Add(manga);

            ctx.Chapters.Add(Seed(manga, "1", null, null));                              // MangaDex+Wiki → vol 1
            ctx.Chapters.Add(Seed(manga, "50", 1, MetadataConfidence.Manual));           // manual (real vol 7) → must hold
            ctx.Chapters.Add(Seed(manga, "85", null, null));                             // MangaDex last → vol 10
            ctx.Chapters.Add(Seed(manga, "86", null, null));                             // beyond MangaDex → Wiki vol 11
            ctx.Chapters.Add(Seed(manga, "139", 17, MetadataConfidence.Heuristic));      // Wiki confirms vol 17
            ctx.Chapters.Add(Seed(manga, "148", 17, MetadataConfidence.Heuristic));      // STALE → Wiki heals to vol 18
            ctx.Chapters.Add(Seed(manga, "201", 17, MetadataConfidence.Heuristic));      // STALE → Wiki heals to vol 23
            ctx.Chapters.Add(Seed(manga, "210", null, null));                            // Wiki vol 24 (beyond 23)
            ctx.Chapters.Add(Seed(manga, "235", null, null));                            // past last volume → stays loose

            mangaKey = manga.Key;
            return Task.CompletedTask;
        });

        var http = new HttpClient(new FakeHttpMessageHandler(Route));
        var worker = new ResolveMissingVolumesForMangaWorker(
            new ConcurrentQueue<string>([mangaKey]), _harness.Settings,
            new MangaDexVolumeResolver(http),               // real
            new MangaDexSearchService(http),                // real (drives auto-match + scoring fix)
            new IVolumeResolver[] { new WikipediaVolumeResolver(http) }); // real

        await worker.DoWork(_harness.CreateScope());

        var byNumber = await _harness.Query(ctx => ctx.Chapters.ToDictionaryAsync(c => c.ChapterNumber, c => c));
        var source = await _harness.Query(ctx => ctx.Set<MetadataSource>().FirstAsync(s => s.MangaId == mangaKey));

        // Auto-match succeeded despite an empty lastChapter (the scoring fix), via the ja-ro title.
        Assert.Equal(MetadataSourceStatus.AutoMatched, source.Status);
        Assert.Equal(DandadanId, source.ExternalId);

        Assert.Equal(1, byNumber["1"].VolumeNumber);
        Assert.Equal(10, byNumber["85"].VolumeNumber);
        Assert.Equal(11, byNumber["86"].VolumeNumber);   // Wikipedia filled the gap MangaDex can't
        Assert.Equal(17, byNumber["139"].VolumeNumber);
        Assert.Equal(18, byNumber["148"].VolumeNumber);  // stale heuristic healed
        Assert.Equal(23, byNumber["201"].VolumeNumber);  // stale heuristic healed
        Assert.Equal(24, byNumber["210"].VolumeNumber);  // resolves past volume 23

        // Manual assignment held even though Wikipedia says chapter 50 is volume 7.
        Assert.Equal(1, byNumber["50"].VolumeNumber);
        Assert.Equal(MetadataConfidence.Manual, byNumber["50"].MetadataConfidence);

        // A chapter past the last published volume has no source and stays loose for manual assignment.
        Assert.Null(byNumber["235"].VolumeNumber);
    }
}
