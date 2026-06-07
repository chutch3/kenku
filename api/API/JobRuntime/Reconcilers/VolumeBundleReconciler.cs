using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using log4net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Level-triggered bundling reconciler: periodically enqueues a <see cref="ReconcileVolumeBundleHandler"/>
/// job for every VolumeCBZ volume that is ready to bundle or whose bundle has gone stale. Replaces the
/// EnsureReadyVolumesBundled + EnsureBundledVolumesFresh polling workers; coalescing is handled by the
/// job's dedup key, so ticks never pile up.
/// </summary>
public class VolumeBundleReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VolumeBundleReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static string DedupKey(string seriesKey, int volume) => $"reconcile-bundle:{seriesKey}:{volume}";

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
            catch (Exception e) { Log.Error("Volume bundle reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a reconcile job for every ready-to-bundle or stale volume. Deduped per (series, volume).</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext series, IJobStore store, DateTime now, CancellationToken ct)
    {
        List<Series> volumeCbz = await series.Series
            .Include(m => m.Chapters)
            .Where(m => m.LibraryLayout == LibraryLayout.VolumeCBZ)
            .ToListAsync(ct);

        Dictionary<string, List<VolumeMetadata>> bundledByManga = (await series.VolumeMetadata
                .Where(v => v.ArchiveFileName != null).ToListAsync(ct))
            .GroupBy(v => v.MangaId).ToDictionary(g => g.Key, g => g.ToList());

        Dictionary<string, HashSet<string>> recordedByVolumeKey = (await series.BundleChapterMaps.ToListAsync(ct))
            .GroupBy(m => m.VolumeKey).ToDictionary(g => g.Key, g => g.Select(m => m.ChapterKey).ToHashSet());

        int enqueued = 0;
        foreach (Series manga in volumeCbz)
        {
            var volumes = new HashSet<int>(VolumeBundlePolicy.VolumesReadyToBundle(manga));

            foreach (VolumeMetadata volume in bundledByManga.GetValueOrDefault(manga.Key, []))
            {
                HashSet<string> recorded = recordedByVolumeKey.GetValueOrDefault(volume.Key, []);
                HashSet<string> desired = manga.Chapters
                    .Where(c => c.VolumeNumber == volume.VolumeNumber && c.Downloaded).Select(c => c.Key).ToHashSet();
                if (!desired.SetEquals(recorded))
                    volumes.Add(volume.VolumeNumber);
            }

            foreach (int volume in volumes)
            {
                await store.EnqueueAsync(new Job(ReconcileVolumeBundleHandler.Type,
                    ReconcileVolumeBundleHandler.PayloadFor(manga.Key, volume), now,
                    resourceKey: manga.Key, dedupKey: DedupKey(manga.Key, volume)), ct);
                enqueued++;
            }
        }

        return enqueued;
    }
}
