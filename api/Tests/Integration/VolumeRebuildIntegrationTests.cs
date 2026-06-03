using System.IO.Compression;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// When a chapter is added to a volume that is already bundled, the volume is rebuilt so the chapter
/// lands in the right position. Driven by the real <see cref="WorkerQueue"/>, so the unbundle→rebundle
/// ordering is enforced by the production dependency scheduler — the correct final bundle is only
/// possible if unbundle actually runs before rebundle.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeRebuildIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("%M - Ch.%C");
    private readonly WorkerQueue _queue;

    public VolumeRebuildIntegrationTests() =>
        _queue = new WorkerQueue(_harness.Services, _harness.Settings);

    public void Dispose() => _harness.Dispose();

    private static byte[] FakeCbz(int pages, string prefix)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
            {
                using var s = zip.CreateEntry($"{prefix}{i:D3}.jpg").Open();
                s.Write([0xFF, 0xD8, 0xFF, 0xD9]);
            }
        return ms.ToArray();
    }

    private static async Task<bool> WaitUntil(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task AddingChapterToBundledVolume_RebuildsItWithAllChaptersInOrder()
    {
        string mangaKey = null!, mangaDir = null!;
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

        // Bundle volume 1 through the real queue and wait for it to settle.
        _queue.AddWorker(new BundleVolumeWorker(mangaKey, 1, _harness.Settings));
        Assert.True(await WaitUntil(
            () => _harness.Query(c => c.VolumeMetadata.AnyAsync(v => v.MangaId == mangaKey && v.ArchiveFileName != null)),
            TimeSpan.FromSeconds(15)), "initial bundle was not produced");

        // A late chapter 3 arrives in the already-bundled volume.
        await _harness.Seed(async ctx =>
        {
            var manga = await ctx.Series.Include(m => m.Library).FirstAsync(m => m.Key == mangaKey);
            ctx.Chapters.Add(new Chapter(manga, "3", 1) { Downloaded = true, FileName = "Test - Ch.3.cbz" });
            File.WriteAllBytes(Path.Combine(mangaDir, "Test - Ch.3.cbz"), FakeCbz(2, "c3p"));
        });

        // The freshness reconciler spawns unbundle → rebundle; the real queue must order them.
        _queue.AddWorker(new EnsureBundledVolumesFreshWorker(_queue, _harness.Settings));
        Assert.True(await WaitUntil(
            () => _harness.Query(async c => await c.BundleChapterMaps.CountAsync() == 3),
            TimeSpan.FromSeconds(20)), "stale bundle was not rebuilt to 3 chapters");

        // Correct final state is only possible if unbundle ran before rebundle.
        string bundlePath = Path.Combine(mangaDir, "Vol 1.cbz");
        Assert.True(File.Exists(bundlePath));
        using (var zip = ZipFile.OpenRead(bundlePath))
            Assert.Equal(6, zip.Entries.Count(e => e.Name.EndsWith(".jpg")));
        var maps = await _harness.Query(c => c.BundleChapterMaps.OrderBy(m => m.StartPage).ToListAsync());
        Assert.Equal([0, 2, 4], maps.Select(m => m.StartPage));
        var chapters = await _harness.Query(c => c.Chapters.ToListAsync());
        Assert.All(chapters, ch => Assert.True(ch.IsBundled));
    }
}
