using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;

namespace API.JobRuntime;

/// <summary>
/// The standard jobs queued whenever a series source should be (re)pulled. Owns the enqueue
/// conventions — dedup keys shared with the reconcilers so manual triggers coalesce with scheduled
/// ticks, and the series as resource key for per-series fairness — so they live in one place instead
/// of being repeated at every call site.
/// </summary>
public static class SeriesJobs
{
    public static async Task EnqueueCoverAndSync(IJobStore jobStore, IClock clock,
        SourceId<Series> source, string language, CancellationToken ct)
    {
        await EnqueueCover(jobStore, clock, source, ct);
        await EnqueueSync(jobStore, clock, source, language, ct);
    }

    public static Task EnqueueCover(IJobStore jobStore, IClock clock, SourceId<Series> source, CancellationToken ct) =>
        jobStore.EnqueueAsync(new Job(DownloadCoverHandler.Type,
            DownloadCoverHandler.PayloadFor(source.Key), clock.UtcNow,
            resourceKey: source.ObjId, dedupKey: CoverRefreshReconciler.DedupKey(source.Key)), ct);

    public static Task EnqueueSync(IJobStore jobStore, IClock clock, SourceId<Series> source, string language, CancellationToken ct) =>
        jobStore.EnqueueAsync(new Job(SyncSeriesChaptersHandler.Type,
            SyncSeriesChaptersHandler.PayloadFor(source.Key, language), clock.UtcNow,
            resourceKey: source.ObjId, dedupKey: SeriesChapterSyncReconciler.DedupKey(source.Key)), ct);
}
