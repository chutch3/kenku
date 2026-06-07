using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="RefreshExternalMetadataHandler"/> job for every tracked series.
/// Replaces the bulk UpdateMetadataWorker; deduped per series.
/// </summary>
public class MetadataRefreshReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(MetadataRefreshReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    public static string DedupKey(string mangaId) => $"refresh-metadata:{mangaId}";

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
            catch (Exception e) { Log.Error("Metadata refresh reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a metadata-refresh job for each tracked series with a metadata entry, deduped.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        List<string> mangaIds = await series.MangaConnectorToManga
            .Where(m => m.UseForDownload)
            .Join(series.MetadataEntries, mcId => mcId.ObjId, e => e.MangaId, (_, e) => e.MangaId)
            .Distinct()
            .ToListAsync(ct);

        foreach (string mangaId in mangaIds)
            await store.EnqueueAsync(new Job(RefreshExternalMetadataHandler.Type,
                RefreshExternalMetadataHandler.PayloadFor(mangaId), now,
                resourceKey: mangaId, dedupKey: DedupKey(mangaId)), ct);

        return mangaIds.Count;
    }
}
