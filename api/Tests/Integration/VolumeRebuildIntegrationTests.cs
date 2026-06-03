using System.IO.Compression;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Proves the rebuild story end-to-end with real zip I/O: a volume is bundled, a chapter is later
/// added to it, and the freshness reconciler's unbundle → rebundle chain produces a single bundle
/// containing all chapters in the correct order. Uses the harness so every worker runs in its own
/// scope, exactly like production.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeRebuildIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("%M - Ch.%C");

    public void Dispose() => _harness.Dispose();

    private static byte[] FakeCbz(int pages, string prefix)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
            {
                using var s = zip.CreateEntry($"{prefix}{i:D3}.jpg").Open();
                s.Write([0xFF, 0xD8, 0xFF, 0xD9]); // minimal JPEG bytes
            }
        return ms.ToArray();
    }

    private EnsureBundledVolumesFreshWorker Reconciler()
    {
        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([]);
        return new EnsureBundledVolumesFreshWorker(queue.Object, _harness.Settings);
    }

    [Fact]
    public async Task AddingChapterToBundledVolume_RebuildsBundleWithAllChaptersInOrder()
    {
        string mangaKey = null!, mangaDir = null!;

        // Seed a VolumeCBZ series with chapters 1 & 2 in volume 1, as real cbz files.
        await _harness.Seed(ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library)
                { LibraryLayout = LibraryLayout.VolumeCBZ };
            ctx.Series.Add(manga);

            mangaDir = manga.FullDirectoryPath;
            Directory.CreateDirectory(mangaDir);
            foreach (int n in new[] { 1, 2 })
            {
                ctx.Chapters.Add(new Chapter(manga, n.ToString(), 1) { Downloaded = true, FileName = $"Test - Ch.{n}.cbz" });
                File.WriteAllBytes(Path.Combine(mangaDir, $"Test - Ch.{n}.cbz"), FakeCbz(2, $"c{n}p"));
            }
            mangaKey = manga.Key;
            return Task.CompletedTask;
        });

        // Bundle volume 1 (chapters 1 & 2).
        await _harness.Run(new BundleVolumeWorker(mangaKey, 1, _harness.Settings));

        // A late chapter 3 is downloaded into the already-bundled volume 1.
        await _harness.Seed(async ctx =>
        {
            var manga = await ctx.Series.Include(m => m.Library).FirstAsync(m => m.Key == mangaKey);
            ctx.Chapters.Add(new Chapter(manga, "3", 1) { Downloaded = true, FileName = "Test - Ch.3.cbz" });
            File.WriteAllBytes(Path.Combine(mangaDir, "Test - Ch.3.cbz"), FakeCbz(2, "c3p"));
        });

        // The reconciler detects the stale bundle and runs unbundle → rebundle.
        await _harness.Run(Reconciler());

        // The single bundle now contains all three chapters' pages (3 × 2).
        string bundlePath = Path.Combine(mangaDir, "Vol 1.cbz");
        Assert.True(File.Exists(bundlePath));
        using (var zip = ZipFile.OpenRead(bundlePath))
            Assert.Equal(6, zip.Entries.Count(e => e.Name.EndsWith(".jpg")));

        // The page map covers 3 chapters in order: 0, 2, 4.
        var maps = await _harness.Query(ctx => ctx.BundleChapterMaps.OrderBy(m => m.StartPage).ToListAsync());
        Assert.Equal(3, maps.Count);
        Assert.Equal([0, 2, 4], maps.Select(m => m.StartPage));

        // Every chapter is marked bundled and the loose files are gone.
        var chapters = await _harness.Query(ctx => ctx.Chapters.ToListAsync());
        Assert.All(chapters, c => Assert.True(c.IsBundled));
        Assert.False(File.Exists(Path.Combine(mangaDir, "Test - Ch.3.cbz")));
    }
}
