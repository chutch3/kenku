using API.DownloadClients.Interfaces;
using API.JobRuntime.Interfaces;
using API.Acquirers;
using API.DownloadClients;
using API.JobRuntime.Handlers;
using API.Connectors;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Polls the download client for every undownloaded torrent-kind chapter and enqueues a
/// <see cref="FinalizeTorrentHandler"/> job for each completed torrent. Replaces the polling half of
/// TorrentCompletionWorker; deduped per source so a still-pending finalise isn't re-queued. Registered
/// only when a download client is configured.
/// </summary>
public class TorrentCompletionReconciler(IServiceScopeFactory scopeFactory, IClock clock,
    IConfiguration configuration, IDownloadClient downloadClient, IEnumerable<SeriesSource> connectors)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromSeconds(30);

    public static string DedupKey(string sourceIdKey) => $"finalize-torrent:{sourceIdKey}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            downloadClient, connectors,
            scope.GetRequiredService<IJobStore>(),
            clock.UtcNow, ct);

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

        // Pack torrents aren't chapter-keyed, so they're discovered from the client itself: any
        // completed download carrying a pack tag gets a FinalizePack job (deduped per tag).
        foreach (DownloadEntry entry in await downloadClient.List(ct))
        {
            if (Acquirers.PackTag.SeriesKeyOf(entry.Tag) is not { } seriesKey) continue;
            if (entry.Status is not DownloadStatus.Completed packCompleted) continue;

            await store.EnqueueAsync(new Job(FinalizePackHandler.Type,
                FinalizePackHandler.PayloadFor(entry.Tag, seriesKey, packCompleted.SavePath), now,
                resourceKey: seriesKey, dedupKey: $"finalize-pack:{entry.Tag}"), ct);
            enqueued++;
        }
        return enqueued;
    }
}
