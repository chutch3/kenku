using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
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

    private async Task<SeriesContext> SeedTracked(int count)
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        for (int i = 0; i < count; i++)
        {
            var s = new Series($"Tracked {i}", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(s);
            ctx.MangaConnectorToManga.Add(new SourceId<Series>(s, "MockConnector", $"t{i}", "url", useForDownload: true));
        }
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Scan_ReArmsAWedgedSyncJob_SoATransientUpstreamFailureDoesNotFreezeSyncing()
    {
        using var ctx = await Seed();
        var trackedSource = await ctx.MangaConnectorToManga.FirstAsync(id => id.UseForDownload);
        var store = new InMemoryJobStore();
        // A prior sync failed all the way to NeedsAttention (e.g. an HTTP 500 from the source).
        var parked = await store.EnqueueAsync(new Job(SyncSeriesChaptersHandler.Type, "{}", DateTime.UtcNow,
            dedupKey: SeriesChapterSyncReconciler.DedupKey(trackedSource.Key)));
        parked.Status = JobStatus.NeedsAttention;
        parked.Attempts = 5;
        parked.Error = "WeebCentral chapter list request failed: HTTP 500";
        await store.UpdateAsync(parked);

        await SeriesChapterSyncReconciler.ScanAndEnqueueAsync(ctx, store, "en", DateTime.UtcNow, default);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task Scan_StaggersTheEnqueuedJobs_SoATickDoesNotBurstEverySourceAtOnce()
    {
        using var ctx = await SeedTracked(4);
        var store = new InMemoryJobStore();

        await SeriesChapterSyncReconciler.ScanAndEnqueueAsync(ctx, store, "en", DateTime.UtcNow, default);

        var due = (await store.GetAllAsync()).Select(j => j.ScheduledFor).OrderBy(d => d).ToList();
        Assert.Equal(4, due.Count);
        // Spread out, not all due at the same instant.
        Assert.True(due[0] < due[^1], "sync jobs should be staggered across a window");
        Assert.Equal(due.Count, due.Distinct().Count());
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
