using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

/// <summary>
/// Level-triggered bundling: periodically bundles every volume that <see cref="VolumeBundlePolicy"/>
/// reports as ready, independent of a download just completing. The old auto-bundle was edge-triggered
/// (only fired from a fresh chapter download), so ready volumes were never bundled on restart, once
/// downloads settled, or for chapters re-recognized via CheckDownloaded. Dedupes against in-flight
/// bundle jobs the same way StartNewChapterDownloadsWorker does. See issue #22.
/// </summary>
public class EnsureReadyVolumesBundledWorker(IWorkerQueue workerQueue, KenkuSettings settings, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    private SeriesContext _mangaContext = null!;

    public DateTime LastExecution { get; set; } = DateTime.MinValue;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        // Volumes already being bundled (queued or running) so we never double-queue one.
        HashSet<(string mangaId, int volume)> inFlight = workerQueue.GetKnownWorkers()
            .OfType<BundleVolumeWorker>()
            .Select(w => (w.MangaId, w.VolumeNumber))
            .ToHashSet();

        List<Series> volumeCbzSeries = await _mangaContext.Series
            .Include(m => m.Chapters)
            .Where(m => m.LibraryLayout == LibraryLayout.VolumeCBZ)
            .ToListAsync(CancellationToken);

        var jobs = new List<BaseWorker>();
        foreach (Series manga in volumeCbzSeries)
        {
            foreach (int volume in VolumeBundlePolicy.VolumesReadyToBundle(manga))
            {
                // HashSet.Add returns false if already present — dedupes both in-flight jobs and any
                // duplicate within this pass.
                if (inFlight.Add((manga.Key, volume)))
                    jobs.Add(new BundleVolumeWorker(manga.Key, volume, settings));
            }
        }

        LastExecution = DateTime.UtcNow;
        return jobs.ToArray();
    }
}
