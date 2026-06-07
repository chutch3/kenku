using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The level-triggered reconciler that replaced EnsureReadyVolumesBundled + EnsureBundledVolumesFresh:
/// it enqueues a ReconcileVolumeBundle job for each ready or stale volume, deduped per (series, volume).
/// </summary>
public class VolumeBundleReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-recon-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("recon-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private Series SeedVolumeCbz(SeriesContext ctx)
    {
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
            [], [], [], [], library) { LibraryLayout = LibraryLayout.VolumeCBZ };
        ctx.Series.Add(manga);
        return manga;
    }

    [Fact]
    public async Task Scan_EnqueuesAReconcileJob_ForAReadyClosedVolume()
    {
        using var ctx = NewContext();
        var manga = SeedVolumeCbz(ctx);
        ctx.Chapters.Add(new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = "ch1.cbz" });
        ctx.Chapters.Add(new Chapter(manga, "2", 2, null) { Downloaded = false }); // vol 2 → vol 1 closed
        await ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        int enqueued = await VolumeBundleReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Equal(1, enqueued);
        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(ReconcileVolumeBundleHandler.Type, job.Type);
        Assert.Equal(VolumeBundleReconciler.DedupKey(manga.Key, 1), job.DedupKey);
        Assert.Equal(manga.Key, job.ResourceKey);
    }

    [Fact]
    public async Task Scan_EnqueuesAReconcileJob_ForAStaleBundledVolume()
    {
        using var ctx = NewContext();
        var manga = SeedVolumeCbz(ctx);
        var ch1 = new Chapter(manga, "1", 1, null) { Downloaded = true, IsBundled = true };
        var ch2 = new Chapter(manga, "2", 1, null) { Downloaded = true, FileName = "ch2.cbz" }; // joined after bundling
        ctx.Chapters.AddRange(ch1, ch2);
        var volume = new VolumeMetadata(manga, 1) { ArchiveFileName = "Vol 1.cbz" };
        ctx.VolumeMetadata.Add(volume);
        ctx.BundleChapterMaps.Add(new BundleChapterMap { VolumeKey = volume.Key, ChapterKey = ch1.Key, StartPage = 0, PageCount = 1 });
        await ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        int enqueued = await VolumeBundleReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Equal(1, enqueued);
        Assert.Equal(VolumeBundleReconciler.DedupKey(manga.Key, 1), (await store.GetAllAsync())[0].DedupKey);
    }

    [Fact]
    public async Task Scan_DoesNothing_WhenEveryBundleIsFreshAndNoVolumeIsReady()
    {
        using var ctx = NewContext();
        var manga = SeedVolumeCbz(ctx);
        var ch1 = new Chapter(manga, "1", 1, null) { Downloaded = true, IsBundled = true };
        ctx.Chapters.Add(ch1);
        ctx.Chapters.Add(new Chapter(manga, "2", 2, null) { Downloaded = false });
        var volume = new VolumeMetadata(manga, 1) { ArchiveFileName = "Vol 1.cbz" };
        ctx.VolumeMetadata.Add(volume);
        ctx.BundleChapterMaps.Add(new BundleChapterMap { VolumeKey = volume.Key, ChapterKey = ch1.Key, StartPage = 0, PageCount = 1 });
        await ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        int enqueued = await VolumeBundleReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Equal(0, enqueued);
        Assert.Empty(await store.GetAllAsync());
    }
}
