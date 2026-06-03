using System.IO.Compression;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MaintenanceWorkers;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Integration test for #22: a volume that is complete and closed bundles even when no new download is
/// happening (on restart, after churn settles, or for chapters re-recognized via CheckDownloaded).
/// Driven by the real <see cref="WorkerQueue"/> so the reconciler's spawned bundle job actually runs.
/// </summary>
[Trait("Category", "Integration")]
public class LevelTriggeredBundlingIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new("%M - Ch.%C");
    private readonly WorkerQueue _queue;

    public LevelTriggeredBundlingIntegrationTests() => _queue = new WorkerQueue(_harness.Services, _harness.Settings);

    public void Dispose() => _harness.Dispose();

    private static byte[] FakeCbz(int pages)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
                using (zip.CreateEntry($"{i:D3}.jpg").Open()) { }
        return ms.ToArray();
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task ReadyVolume_IsBundled_WithoutANewDownload()
    {
        await _harness.Seed(async ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
                [], [], [], [], library) { LibraryLayout = LibraryLayout.VolumeCBZ };
            ctx.Series.Add(manga);

            string dir = Path.Combine(_harness.TempDir, manga.DirectoryName);
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

        _queue.AddWorker(new EnsureReadyVolumesBundledWorker(_queue, _harness.Settings));

        string bundle = Path.Combine(_harness.TempDir, "Test Series", "Vol 1.cbz");
        Assert.True(await WaitUntil(() => File.Exists(bundle), TimeSpan.FromSeconds(15)),
            $"Volume 1 should have bundled to {bundle} with no new download");
    }
}
