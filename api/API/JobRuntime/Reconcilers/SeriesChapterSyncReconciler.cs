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

    /// <summary>Enqueues a sync job for each tracked series-connector, deduped, keyed on the series.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, string language,
        DateTime now, CancellationToken ct)
    {
        List<SourceId<Series>> tracked = await series.MangaConnectorToManga
            .Where(id => id.UseForDownload)
            .ToListAsync(ct);

        foreach (SourceId<Series> mangaConnectorId in tracked)
            await store.EnqueueAsync(new Job(SyncSeriesChaptersHandler.Type,
                SyncSeriesChaptersHandler.PayloadFor(mangaConnectorId.Key, language), now,
                resourceKey: mangaConnectorId.ObjId, dedupKey: DedupKey(mangaConnectorId.Key)), ct);

        return tracked.Count;
    }
}
