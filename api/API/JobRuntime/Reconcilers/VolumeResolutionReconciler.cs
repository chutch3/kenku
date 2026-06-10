using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="ResolveSeriesVolumesHandler"/> job for every series that still has a
/// chapter without a volume number. Replaces the ResolveMissingVolumes fan-out worker + IBatchWorkerFactory;
/// the dispatcher's per-resource cap (keyed on the metadata host) provides the parallelism the old worker
/// got from its pool, and the job dedup key keeps ticks from piling up.
/// </summary>
public class VolumeResolutionReconciler(
    IServiceScopeFactory scopeFactory, IClock clock, KenkuSettings settings, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromDays(1);

    public const string ResourceKey = "mangadex";
    public static string DedupKey(string seriesKey) => $"resolve-volumes:{seriesKey}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            settings, clock.UtcNow, ct);

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
