using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="RefreshExternalMetadataHandler"/> job for every tracked series.
/// Replaces the bulk UpdateMetadataWorker; deduped per series.
/// </summary>
public class MetadataRefreshReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(12);

    public static string DedupKey(string mangaId) => $"refresh-metadata:{mangaId}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            clock.UtcNow, ct);

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
