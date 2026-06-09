using API.DownloadClients.Interfaces;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Connectors;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="DownloadChapterHandler"/> job for every chapter wanted but not yet
/// downloaded. Replaces <c>StartNewChapterDownloadsWorker</c>: dedup (per chapter source-id) stops a
/// chapter being queued twice, the resource key (the series) gives per-series fairness so one big series
/// can't hog the pool (AF2c), and overall throughput is bounded by the dispatcher's caps + the per-host
/// rate limiter — no more blind re-queue loop (#31).
/// </summary>
public class DownloadReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DownloadReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    public static string DedupKey(string sourceIdKey) => $"download:{sourceIdKey}";

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
                    clock.UtcNow,
                    scope.ServiceProvider.GetService<IDownloadClient>(),
                    scope.ServiceProvider.GetServices<SeriesSource>(),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Download reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>
    /// Enqueues a download job for each missing chapter, deduped per source-id, keyed on its series.
    /// Torrent-kind chapters whose torrent is already in the download client are skipped — they are not
    /// missing, they are in flight, and <see cref="TorrentCompletionReconciler"/> owns their completion.
    /// </summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now,
        IDownloadClient? downloadClient, IEnumerable<SeriesSource> connectors, CancellationToken ct)
    {
        List<SourceId<Chapter>> missing = await ChapterDownloadService.GetMissingChapters(series, ct);

        var torrentSourceNames = connectors
            .Where(c => c.Kind == Acquirers.AcquisitionKind.Torrent)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int enqueued = 0;
        foreach (SourceId<Chapter> chapterSourceId in missing)
        {
            if (downloadClient is not null && await TorrentInFlight(chapterSourceId))
                continue;

            await store.EnqueueAsync(new Job(DownloadChapterHandler.Type,
                DownloadChapterHandler.PayloadFor(chapterSourceId.Key), now,
                resourceKey: chapterSourceId.Obj.ParentMangaId, dedupKey: DedupKey(chapterSourceId.Key)), ct);
            enqueued++;
        }
        return enqueued;

        async Task<bool> TorrentInFlight(SourceId<Chapter> chId)
        {
            if (!torrentSourceNames.Contains(chId.MangaConnectorName))
                return false;
            try
            {
                return await downloadClient.GetStatus(chId.Key, ct) is DownloadStatus.Downloading or DownloadStatus.Completed;
            }
            catch (Exception e)
            {
                // Client unreachable: enqueue anyway — the download job surfaces the error, bounded.
                Log.Warn($"Could not query download client for {chId.Key}: {e.Message}");
                return false;
            }
        }
    }
}
