using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="RefreshExternalMetadataHandler"/> job for every tracked series.
/// Replaces the bulk UpdateMetadataWorker; deduped per series.
/// </summary>
public class MetadataRefreshReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(12);

    public static string DedupKey(string seriesId) => $"refresh-metadata:{seriesId}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            clock.UtcNow, ct);

    /// <summary>Enqueues a metadata-refresh job for each tracked series with a metadata entry, deduped.
    /// Finished (Completed/Cancelled) series are skipped: a series only has an entry once it's been linked
    /// (which fetches inline), so its external metadata is already on hand and won't change — re-enqueuing
    /// it every 12h is wasted load. Mirrors <see cref="SeriesChapterSyncReconciler"/>'s finished-series gate.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        HashSet<string> finished = (await series.Series
            .Where(s => s.ReleaseStatus == SeriesReleaseStatus.Completed || s.ReleaseStatus == SeriesReleaseStatus.Cancelled)
            .Select(s => s.Key)
            .ToListAsync(ct)).ToHashSet();

        List<string> mangaIds = (await series.SeriesSourceIds
            .Where(m => m.UseForDownload)
            .Join(series.MetadataEntries, mcId => mcId.ObjId, e => e.SeriesId, (_, e) => e.SeriesId)
            .Distinct()
            .ToListAsync(ct))
            .Where(id => !finished.Contains(id))
            .ToList();

        foreach (string seriesId in mangaIds)
            await store.EnqueueAsync(new Job(RefreshExternalMetadataHandler.Type,
                RefreshExternalMetadataHandler.PayloadFor(seriesId), now,
                resourceKey: seriesId, dedupKey: DedupKey(seriesId)), ct);

        return mangaIds.Count;
    }
}
