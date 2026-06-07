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
/// Periodically enqueues a <see cref="ResolveSeriesVolumesHandler"/> job for every series that still has a
/// chapter without a volume number. Replaces the ResolveMissingVolumes fan-out worker + IBatchWorkerFactory;
/// the dispatcher's per-resource cap (keyed on the metadata host) provides the parallelism the old worker
/// got from its pool, and the job dedup key keeps ticks from piling up.
/// </summary>
public class VolumeResolutionReconciler(
    IServiceScopeFactory scopeFactory, IClock clock, KenkuSettings settings, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VolumeResolutionReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    public const string ResourceKey = "mangadex";
    public static string DedupKey(string seriesKey) => $"resolve-volumes:{seriesKey}";

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
                    settings, clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Volume resolution reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a resolve job per series with an unresolved chapter. No-op when resolution is disabled.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, KenkuSettings settings,
        DateTime now, CancellationToken ct)
    {
        if (settings.VolumeResolutionStrategy == VolumeResolutionStrategy.Disabled)
            return 0;

        List<string> mangaIds = await series.Chapters
            .Where(c => c.VolumeNumber == null)
            .Select(c => c.ParentMangaId)
            .Distinct()
            .ToListAsync(ct);

        foreach (string mangaId in mangaIds)
            await store.EnqueueAsync(new Job(ResolveSeriesVolumesHandler.Type,
                ResolveSeriesVolumesHandler.PayloadFor(mangaId), now,
                resourceKey: ResourceKey, dedupKey: DedupKey(mangaId)), ct);

        return mangaIds.Count;
    }
}
