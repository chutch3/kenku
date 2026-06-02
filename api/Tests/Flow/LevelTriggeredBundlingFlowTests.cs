using System.IO.Compression;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace API.Tests.Flow;

/// <summary>
/// Flow test for #22: a volume that is complete and closed must bundle even when no new download is
/// happening (on restart, after churn settles, or for chapters re-recognized via CheckDownloaded).
/// The old edge-triggered path only fired from a fresh download completion, so this never happened.
/// </summary>
public class LevelTriggeredBundlingFlowTests
{
    private static byte[] FakeCbz(int pages)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
                using (zip.CreateEntry($"{i:D3}.jpg").Open()) { }
        return ms.ToArray();
    }

    [Fact]
    public async Task ReadyVolume_IsBundled_WithoutANewDownload()
    {
        using var harness = new FlowTestHarness("%M - Ch.%C");

        await harness.Seed(async ctx =>
        {
            var library = new FileLibrary(harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
                [], [], [], [], library);
            manga.LibraryLayout = LibraryLayout.VolumeCBZ;
            ctx.Series.Add(manga);

            string dir = Path.Combine(harness.TempDir, manga.DirectoryName);
            Directory.CreateDirectory(dir);
            // Volume 1: complete (2 chapters, real files on disk). No VolumeMetadata seeded.
            for (int i = 1; i <= 2; i++)
            {
                ctx.Chapters.Add(new Chapter(manga, i.ToString(), 1, null) { Downloaded = true, FileName = $"ch{i}.cbz" });
                await File.WriteAllBytesAsync(Path.Combine(dir, $"ch{i}.cbz"), FakeCbz(3));
            }
            // Volume 2 exists → volume 1 is closed. Nothing is downloading.
            ctx.Chapters.Add(new Chapter(manga, "3", 2, null) { Downloaded = false });
        });

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([]);

        await harness.Run(new EnsureReadyVolumesBundledWorker(queue.Object, harness.Settings));

        string bundle = Path.Combine(harness.TempDir, "Test Series", "Vol 1.cbz");
        Assert.True(File.Exists(bundle), $"Volume 1 should have bundled to {bundle} with no new download");
    }

    [Fact]
    public async Task DoesNotQueue_AVolumeAlreadyInFlight()
    {
        using var harness = new FlowTestHarness("%M - Ch.%C");
        string mangaKey = null!;
        await harness.Seed(async ctx =>
        {
            var library = new FileLibrary(harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("S", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            manga.LibraryLayout = LibraryLayout.VolumeCBZ;
            ctx.Series.Add(manga);
            mangaKey = manga.Key;
            ctx.Chapters.Add(new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = "ch1.cbz" });
            ctx.Chapters.Add(new Chapter(manga, "2", 2, null) { Downloaded = false });
            await Task.CompletedTask;
        });

        var queue = new Mock<IWorkerQueue>();
        queue.Setup(q => q.GetKnownWorkers()).Returns([new BundleVolumeWorker(mangaKey, 1, harness.Settings)]);

        var worker = new EnsureReadyVolumesBundledWorker(queue.Object, harness.Settings);
        BaseWorker[] spawned = await worker.DoWork(harness.CreateScope());

        Assert.DoesNotContain(spawned.OfType<BundleVolumeWorker>(), b => b.MangaId == mangaKey && b.VolumeNumber == 1);
    }
}
