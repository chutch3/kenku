using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The reconciler that replaced the ResolveMissingVolumes fan-out worker: it enqueues one
/// ResolveSeriesVolumes job per series with an unresolved chapter, deduped, and skips when disabled.
/// </summary>
public class VolumeResolutionReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-rvr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("rvr-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private async Task<SeriesContext> SeedSeriesWithUnresolvedChapters(int seriesCount)
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        for (int i = 0; i < seriesCount; i++)
        {
            var manga = new Series($"Series {i}", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true });
        }
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Scan_EnqueuesOneResolveJobPerSeriesWithUnresolvedChapters()
    {
        using var ctx = await SeedSeriesWithUnresolvedChapters(3);
        var store = new InMemoryJobStore();

        int enqueued = await VolumeResolutionReconciler.ScanAndEnqueueAsync(
            ctx, store, new KenkuSettings(), DateTime.UtcNow, default);

        Assert.Equal(3, enqueued);
        var jobs = await store.GetAllAsync();
        Assert.Equal(3, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(ResolveSeriesVolumesHandler.Type, j.Type));
        Assert.All(jobs, j => Assert.Equal(VolumeResolutionReconciler.ResourceKey, j.ResourceKey));
    }

    [Fact]
    public async Task Scan_DoesNothing_WhenResolutionIsDisabled()
    {
        using var ctx = await SeedSeriesWithUnresolvedChapters(2);
        var store = new InMemoryJobStore();

        int enqueued = await VolumeResolutionReconciler.ScanAndEnqueueAsync(
            ctx, store, new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.Disabled },
            DateTime.UtcNow, default);

        Assert.Equal(0, enqueued);
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_IsDeduped_SoRepeatedTicksDoNotPileUp()
    {
        using var ctx = await SeedSeriesWithUnresolvedChapters(2);
        var store = new InMemoryJobStore();
        var settings = new KenkuSettings();

        await VolumeResolutionReconciler.ScanAndEnqueueAsync(ctx, store, settings, DateTime.UtcNow, default);
        await VolumeResolutionReconciler.ScanAndEnqueueAsync(ctx, store, settings, DateTime.UtcNow, default);

        Assert.Equal(2, (await store.GetAllAsync()).Count); // second tick coalesced on dedup key
    }
}
