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
/// Periodically enqueues a <see cref="DownloadCoverHandler"/> job for every wanted series source.
/// Replaces the UpdateCovers worker fan-out; deduped per source.
/// </summary>
public class CoverRefreshReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CoverRefreshReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public static string DedupKey(string sourceIdKey) => $"cover:{sourceIdKey}";

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
            catch (Exception e) { Log.Error("Cover refresh reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a cover-download job for each wanted series source, deduped.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        var ids = await series.MangaConnectorToManga
            .Where(m => m.UseForDownload)
            .Select(m => new { m.Key, m.ObjId })
            .ToListAsync(ct);

        foreach (var id in ids)
            await store.EnqueueAsync(new Job(DownloadCoverHandler.Type,
                DownloadCoverHandler.PayloadFor(id.Key), now,
                resourceKey: id.ObjId, dedupKey: DedupKey(id.Key)), ct);

        return ids.Count;
    }
}
