using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The reconciler that replaced CheckForNewChapters: it enqueues a SyncSeriesChapters job per tracked
/// series-connector, deduped, so newly-released chapters get pulled in without piling up duplicate jobs.
/// </summary>
public class SeriesChapterSyncReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-scr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("scr-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private async Task<SeriesContext> Seed()
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var tracked = new Series("Tracked", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        var untracked = new Series("Untracked", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        ctx.Series.AddRange(tracked, untracked);
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(tracked, "MockConnector", "t1", "url", useForDownload: true));
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(untracked, "MockConnector", "u1", "url", useForDownload: false));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Scan_EnqueuesASyncJob_OnlyForTrackedSeries()
    {
        using var ctx = await Seed();
        var store = new InMemoryJobStore();

        int enqueued = await SeriesChapterSyncReconciler.ScanAndEnqueueAsync(ctx, store, "en", DateTime.UtcNow, default);

        Assert.Equal(1, enqueued);
        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(SyncSeriesChaptersHandler.Type, job.Type);
    }

    [Fact]
    public async Task Scan_IsDeduped_SoTicksDoNotPileUp()
    {
        using var ctx = await Seed();
        var store = new InMemoryJobStore();

        await SeriesChapterSyncReconciler.ScanAndEnqueueAsync(ctx, store, "en", DateTime.UtcNow, default);
        await SeriesChapterSyncReconciler.ScanAndEnqueueAsync(ctx, store, "en", DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync());
    }
}
