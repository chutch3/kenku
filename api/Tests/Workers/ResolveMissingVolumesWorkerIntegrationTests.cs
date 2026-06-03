using System.Collections.Concurrent;
using API;
using API.Schema.SeriesContext;
using API.Services;
using API.Tests.Integration;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Workers;

/// <summary>
/// Volume resolution through the real worker, resolver, search and heuristic — only the MangaDex HTTP
/// boundary is simulated (WireMock), with responses captured verbatim from the live API (Fixtures/).
/// Covers resolving real aggregate data and falling back to the cover heuristic when a source has none.
/// </summary>
[Trait("Category", "Integration")]
public class ResolveMissingVolumesWorkerIntegrationTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly IntegrationHarness _harness = new("%M - Ch.%C");

    public void Dispose() { _server.Stop(); _harness.Dispose(); }

    private static string Fixture(string series, string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", series, name));

    private static IResponseBuilder Ok(string body) => Response.Create().WithStatusCode(200).WithBody(body);

    // English aggregate is authoritative; the all-languages aggregate fills gaps (and for Berserk
    // disagrees about chapter 1) — serve each so the resolver's en-first merge runs for real.
    private void StubAggregates(string series)
    {
        _server.Given(Request.Create().WithUrl(new WildcardMatcher("*/aggregate*translatedLanguage*")).UsingGet())
            .AtPriority(1).RespondWith(Ok(Fixture(series, "aggregate-en.json")));
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .AtPriority(2).RespondWith(Ok(Fixture(series, "aggregate-all.json")));
    }

    private void StubAggregateEmpty() =>
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Ok("""{ "volumes": {} }"""));

    private HttpClient Http() => new(new HostRewritingHandler(_server.Url!));

    private ResolveMissingVolumesForMangaWorker Worker(string mangaKey, HttpClient http) =>
        new(new ConcurrentQueue<string>([mangaKey]), _harness.Settings,
            new MangaDexVolumeResolver(http), new MangaDexSearchService(http), []);

    [Fact]
    public async Task ResolvesChapterVolumesFromTheMangaDexAggregate()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly;
        StubAggregates("Berserk");

        string key = await SeedLinkedSeries("Berserk", ch => ch("1"));

        await Worker(key, Http()).DoWork(_harness.CreateScope());

        // English tags chapter 1 as volume 5; the all-languages aggregate disagrees (volume 1) — en wins.
        var ch1 = await _harness.Query(c => c.Chapters.FirstAsync(x => x.ChapterNumber == "1"));
        Assert.Equal(5, ch1.VolumeNumber);
    }

    [Fact]
    public async Task MangaDexResolver_ParsesRealAggregateIntoChapterVolumeMap()
    {
        StubAggregates("Berserk");
        var manga = new Series("Berserk", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [],
            new FileLibrary(_harness.TempDir, "Lib"));
        manga.SourceIds.Add(new SourceId<Series>(manga, "MangaDex", "berserk-id", null));

        var map = await new MangaDexVolumeResolver(Http()).GetChapterToVolumeMapAsync(manga);

        Assert.Equal(5, map["1"]);
    }

    [Fact]
    public async Task WhenAMetadataSourceHasNoVolumeData_TheCoverHeuristicResolvesFromImages()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess;
        StubAggregateEmpty(); // the source returns no volume data

        string key = await SeedLinkedSeries("One-Punch Man", ch => ch("1"), mangaDir =>
            // a real, color manga cover → the heuristic should detect it and assign volume 1
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OnePunchMan", "cover.cbz"),
                Path.Combine(mangaDir, "One-Punch Man - Ch.1.cbz")));

        await Worker(key, Http()).DoWork(_harness.CreateScope());

        var ch1 = await _harness.Query(c => c.Chapters.FirstAsync(x => x.ChapterNumber == "1"));
        Assert.Equal(1, ch1.VolumeNumber);
    }

    private async Task<string> SeedLinkedSeries(string name, Action<Action<string>> addChapters, Action<string>? withFiles = null)
    {
        string key = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series(name, "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.NoMatch; // skip auto-match; resolve via connector id
            manga.SourceIds.Add(new SourceId<Series>(manga, "MangaDex", "connector-id", null));
            ctx.Series.Add(manga);

            string dir = manga.FullDirectoryPath;
            Directory.CreateDirectory(dir);
            addChapters(number =>
                ctx.Chapters.Add(new Chapter(manga, number, null, null)
                    { Downloaded = true, FileName = $"{name} - Ch.{number}.cbz" }));
            withFiles?.Invoke(dir);

            key = manga.Key;
            return Task.CompletedTask;
        });
        return key;
    }
}
