using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using log4net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime;

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
                    clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Download reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a download job for each missing chapter, deduped per source-id, keyed on its series.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        List<SourceId<Chapter>> missing = await ChapterDownloadService.GetMissingChapters(series, ct);

        foreach (SourceId<Chapter> chapterSourceId in missing)
            await store.EnqueueAsync(new Job(DownloadChapterHandler.Type,
                DownloadChapterHandler.PayloadFor(chapterSourceId.Key), now,
                resourceKey: chapterSourceId.Obj.ParentMangaId, dedupKey: DedupKey(chapterSourceId.Key)), ct);

        return missing.Count;
    }
}
