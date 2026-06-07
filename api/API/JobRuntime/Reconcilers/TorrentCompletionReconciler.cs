using API.JobRuntime.Interfaces;
using API.Acquirers;
using API.DownloadClients;
using API.JobRuntime.Handlers;
using API.MangaConnectors;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Polls the download client for every undownloaded torrent-kind chapter and enqueues a
/// <see cref="FinalizeTorrentHandler"/> job for each completed torrent. Replaces the polling half of
/// TorrentCompletionWorker; deduped per source so a still-pending finalise isn't re-queued. Registered
/// only when a download client is configured.
/// </summary>
public class TorrentCompletionReconciler(IServiceScopeFactory scopeFactory, IClock clock,
    IConfiguration configuration, IDownloadClient downloadClient, IEnumerable<SeriesSource> connectors)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorrentCompletionReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static string DedupKey(string sourceIdKey) => $"finalize-torrent:{sourceIdKey}";

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
                    downloadClient, connectors,
                    scope.ServiceProvider.GetRequiredService<IJobStore>(),
                    clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Torrent completion reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a finalise job for each completed torrent-kind chapter, deduped per source.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IDownloadClient downloadClient,
        IEnumerable<SeriesSource> connectors, IJobStore store, DateTime now, CancellationToken ct)
    {
        var torrentSourceNames = connectors
            .Where(c => c.Kind == AcquisitionKind.Torrent)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (torrentSourceNames.Count == 0)
            return 0;

        List<SourceId<Chapter>> pending = await series.MangaConnectorToChapter
            .Include(id => id.Obj)
            .Where(id => !id.Obj.Downloaded && id.UseForDownload)
            .ToListAsync(ct);

        int enqueued = 0;
        foreach (SourceId<Chapter> chId in pending)
        {
            if (!torrentSourceNames.Contains(chId.MangaConnectorName)) continue;
            if (await downloadClient.GetStatus(chId.Key, ct) is not DownloadStatus.Completed completed) continue;

            await store.EnqueueAsync(new Job(FinalizeTorrentHandler.Type,
                FinalizeTorrentHandler.PayloadFor(chId.Key, completed.SavePath), now,
                resourceKey: chId.Obj.ParentMangaId, dedupKey: DedupKey(chId.Key)), ct);
            enqueued++;
        }
        return enqueued;
    }
}
