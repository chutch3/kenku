using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="SyncSeriesChaptersHandler"/> job for every tracked series, to pull
/// in newly-released chapters. Replaces <c>CheckForNewChaptersWorker</c>; deduped per series-connector so
/// ticks coalesce.
/// </summary>
public class SeriesChapterSyncReconciler(
    IServiceScopeFactory scopeFactory, IClock clock, KenkuSettings settings, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{

    public static string DedupKey(string sourceIdKey) => $"sync-chapters:{sourceIdKey}";

    protected override TimeSpan Interval => Constants.CheckForNewChaptersInterval;

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            settings.DownloadLanguage, clock.UtcNow, ct);

    /// <summary>The enqueued syncs are spread over this window so a tick doesn't fire every source's
    /// request at the same instant (gentler on the sources, less thundering-herd on the dispatcher).</summary>
    private static readonly TimeSpan StaggerWindow = TimeSpan.FromMinutes(10);

    /// <summary>Enqueues a sync job for each tracked series-connector, deduped, keyed on the series.
    /// A job parked in NeedsAttention from a past (often transient) upstream failure is re-armed rather
    /// than left wedged — otherwise the dedup would coalesce onto it and the series never re-syncs.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, string language,
        DateTime now, CancellationToken ct)
    {
        List<SourceId<Series>> tracked = await series.MangaConnectorToManga
            .Where(id => id.UseForDownload)
            .ToListAsync(ct);

        Dictionary<string, Job> parked = (await store.GetAllAsync(ct))
            .Where(j => j.Type == SyncSeriesChaptersHandler.Type && j.Status == JobStatus.NeedsAttention && j.DedupKey != null)
            .GroupBy(j => j.DedupKey!)
            .ToDictionary(g => g.Key, g => g.First());

        TimeSpan stride = tracked.Count > 1 ? StaggerWindow / (tracked.Count - 1) : TimeSpan.Zero;

        for (int i = 0; i < tracked.Count; i++)
        {
            SourceId<Series> mangaConnectorId = tracked[i];
            DateTime due = now + stride * i;
            if (parked.TryGetValue(DedupKey(mangaConnectorId.Key), out Job? wedged))
            {
                wedged.Status = JobStatus.Queued;
                wedged.Attempts = 0;
                wedged.ScheduledFor = due;
                wedged.Error = null;
                await store.UpdateAsync(wedged, ct);
                continue;
            }

            await store.EnqueueAsync(new Job(SyncSeriesChaptersHandler.Type,
                SyncSeriesChaptersHandler.PayloadFor(mangaConnectorId.Key, language), due,
                resourceKey: mangaConnectorId.ObjId, dedupKey: DedupKey(mangaConnectorId.Key)), ct);
        }

        return tracked.Count;
    }
}
