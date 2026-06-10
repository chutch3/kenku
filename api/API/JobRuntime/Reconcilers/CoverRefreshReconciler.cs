using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="DownloadCoverHandler"/> job for every wanted series source.
/// Replaces the UpdateCovers worker fan-out; deduped per source.
/// </summary>
public class CoverRefreshReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    public static string DedupKey(string sourceIdKey) => $"cover:{sourceIdKey}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            clock.UtcNow, ct);

    /// <summary>Enqueues a cover-download job for each wanted series source, deduped.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        // Skip sources whose series has no cover URL (e.g. indexer/torrent-sourced): there is nothing to
        // download, so enqueuing would only create jobs that fail their way to NeedsAttention.
        var ids = await series.MangaConnectorToManga
            .Where(m => m.UseForDownload && m.Obj.CoverUrl != null && m.Obj.CoverUrl != "")
            .Select(m => new { m.Key, m.ObjId })
            .ToListAsync(ct);

        foreach (var id in ids)
            await store.EnqueueAsync(new Job(DownloadCoverHandler.Type,
                DownloadCoverHandler.PayloadFor(id.Key), now,
                resourceKey: id.ObjId, dedupKey: DedupKey(id.Key)), ct);

        return ids.Count;
    }
}
