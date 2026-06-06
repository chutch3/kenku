using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime;

/// <summary>
/// Periodically enqueues a <see cref="SyncSeriesChaptersHandler"/> job for every tracked series, to pull
/// in newly-released chapters. Replaces <c>CheckForNewChaptersWorker</c>; deduped per series-connector so
/// ticks coalesce.
/// </summary>
public class SeriesChapterSyncReconciler(
    IServiceScopeFactory scopeFactory, IClock clock, KenkuSettings settings, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SeriesChapterSyncReconciler));

    public static string DedupKey(string sourceIdKey) => $"sync-chapters:{sourceIdKey}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Kenku:RunStartup", true))
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                await ScanAndEnqueueAsync(
                    scope.ServiceProvider.GetRequiredService<SeriesContext>(),
                    scope.ServiceProvider.GetRequiredService<IJobStore>(),
                    settings.DownloadLanguage, clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Series chapter sync reconciler error", e); }

            await Task.Delay(Constants.CheckForNewChaptersInterval, stoppingToken);
        }
    }

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
