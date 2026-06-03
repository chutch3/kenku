using System.Collections.Concurrent;
using System.IO.Compression;
using API;
using API.Schema.SeriesContext;
using API.Services;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Reproduces the original Dandadan failure end-to-end through the real worker, fresh scope, and real
/// cover images: an exact source resolves the early chapters, the color heuristic handles the rest in
/// the SAME run (the exact+heuristic composition), and a long black-and-white run does NOT get
/// fabricated into a giant volume — those chapters are left loose. Only the MangaDex HTTP call is
/// simulated (WireMock); the resolver, merger, and heuristic are all real.
/// </summary>
[Trait("Category", "Integration")]
public class HeuristicCompositionIntegrationTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly IntegrationHarness _harness = new();

    public HeuristicCompositionIntegrationTests() =>
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess;

    public void Dispose() { _server.Stop(); _harness.Dispose(); }

    // Exact source: MangaDex aggregate that only covers chapters 1–3 (volume 1).
    private void StubAggregateCoveringChapters1To3() =>
        _server.Given(Request.Create().WithPath(new WildcardMatcher("/manga/*/aggregate")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(
                """{ "volumes": { "1": { "volume": "1", "chapters": { "1": { "chapter": "1" }, "2": { "chapter": "2" }, "3": { "chapter": "3" } } } } }"""));

    private static void WriteImage(string path, byte r, byte g, byte b)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var stream = zip.CreateEntry("01.jpg").Open();
        using var img = new Image<Rgb24>(10, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                img[x, y] = new Rgb24(r, g, b);
        img.SaveAsJpeg(stream);
    }

    private static void ColorCover(string path) => WriteImage(path, 255, 0, 0);
    private static void GrayCover(string path) => WriteImage(path, 128, 128, 128);

    [Fact]
    public async Task ExactResolvesEarly_HeuristicResolvesRest_AndLongBwRunStaysLooseNotABogusVolume()
    {
        string mangaKey = null!;
        string mangaDir = null!;
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Heuristic Series", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.MetadataSource!.Status = MetadataSourceStatus.AutoMatched; // already linked → skip auto-match
            manga.MetadataSource.ExternalId = "linked-id";
            ctx.Series.Add(manga);

            mangaDir = manga.FullDirectoryPath;
            Directory.CreateDirectory(mangaDir);

            // 1–3: resolved by the exact source (no image needed — the heuristic won't touch them).
            for (int n = 1; n <= 3; n++)
                ctx.Chapters.Add(new Chapter(manga, n.ToString(), null, null) { Downloaded = true, FileName = $"ch{n}.cbz" });

            // 4–50: heuristic territory. A small valid volume (color cover 4, then 5–6), a new cover at 7,
            // then a long black-and-white run (8–50) with no further cover → boundary lost → must abort.
            void Add(int n, bool color)
            {
                ctx.Chapters.Add(new Chapter(manga, n.ToString(), null, null) { Downloaded = true, FileName = $"ch{n}.cbz" });
                string path = Path.Combine(mangaDir, $"ch{n}.cbz");
                if (color) ColorCover(path); else GrayCover(path);
            }
            Add(4, color: true);
            Add(5, color: false);
            Add(6, color: false);
            Add(7, color: true);
            for (int n = 8; n <= 50; n++) Add(n, color: false); // 44-chapter run > MaxPlausibleVolumeChapters

            mangaKey = manga.Key;
            return Task.CompletedTask;
        });

        var http = new HttpClient(new HostRewritingHandler(_server.Url!));
        StubAggregateCoveringChapters1To3();
        var worker = new ResolveMissingVolumesForMangaWorker(
            new ConcurrentQueue<string>([mangaKey]), _harness.Settings,
            new MangaDexVolumeResolver(http), new MangaDexSearchService(http), []);

        await worker.DoWork(_harness.CreateScope());

        var ch = await _harness.Query(c => c.Chapters.ToDictionaryAsync(x => x.ChapterNumber, x => x));

        // Exact source resolved 1–3.
        foreach (var n in new[] { "1", "2", "3" })
        {
            Assert.Equal(1, ch[n].VolumeNumber);
            Assert.Equal(MetadataConfidence.Exact, ch[n].MetadataConfidence);
        }

        // Heuristic resolved the small closed volume (4–6) in the SAME run — composition.
        foreach (var n in new[] { "4", "5", "6" })
        {
            Assert.Equal(2, ch[n].VolumeNumber);
            Assert.Equal(MetadataConfidence.Heuristic, ch[n].MetadataConfidence);
        }

        // The long black-and-white run is NOT fabricated into one giant volume — left loose.
        foreach (var n in new[] { "7", "8", "30", "50" })
            Assert.Null(ch[n].VolumeNumber);
    }
}
