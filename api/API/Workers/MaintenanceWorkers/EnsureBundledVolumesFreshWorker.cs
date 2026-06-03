using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

/// <summary>
/// Level-triggered rebuild: an already-bundled volume can go stale when its chapter set changes — a
/// late chapter finishes downloading, a chapter is re-assigned to it, or one is moved out (manually or
/// by re-resolution). This periodically compares each bundle's recorded membership
/// (<see cref="BundleChapterMap"/>) against the chapters that currently belong to the volume and, on a
/// mismatch, queues an unbundle → rebundle so the chapter lands in the right position. The bundle is
/// the source of truth (<see cref="UnbundleVolumeWorker"/> reconstructs chapters from it), so no
/// original files are required. Dedupes against in-flight (un)bundle jobs. Companion to
/// <see cref="EnsureReadyVolumesBundledWorker"/>.
/// </summary>
public class EnsureBundledVolumesFreshWorker(IWorkerQueue workerQueue, KenkuSettings settings, IEnumerable<BaseWorker>? dependsOn = null)
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
        // A volume with an (un)bundle already queued or running is left alone this pass.
        HashSet<(string mangaId, int volume)> inFlight = workerQueue.GetKnownWorkers()
            .SelectMany(w => w switch
            {
                BundleVolumeWorker b => new[] { (b.MangaId, b.VolumeNumber) },
                UnbundleVolumeWorker u => new[] { (u.MangaId, u.VolumeNumber) },
                _ => [],
            })
            .ToHashSet();

        List<Series> volumeCbzSeries = await _mangaContext.Series
            .Include(m => m.Chapters)
            .Where(m => m.LibraryLayout == LibraryLayout.VolumeCBZ)
            .ToListAsync(CancellationToken);

        // Bundled volumes (ArchiveFileName set) and their recorded chapter membership.
        var bundledByManga = (await _mangaContext.VolumeMetadata
                .Where(v => v.ArchiveFileName != null)
                .ToListAsync(CancellationToken))
            .GroupBy(v => v.MangaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var recordedByVolumeKey = (await _mangaContext.BundleChapterMaps.ToListAsync(CancellationToken))
            .GroupBy(m => m.VolumeKey)
            .ToDictionary(g => g.Key, g => g.Select(m => m.ChapterKey).ToHashSet());

        var jobs = new List<BaseWorker>();
        foreach (Series manga in volumeCbzSeries)
        {
            if (!bundledByManga.TryGetValue(manga.Key, out var bundledVolumes))
                continue;

            foreach (VolumeMetadata volume in bundledVolumes)
            {
                // What the bundle currently contains vs. what should be in this volume now.
                var recorded = recordedByVolumeKey.GetValueOrDefault(volume.Key, []);
                var desired = manga.Chapters
                    .Where(c => c.VolumeNumber == volume.VolumeNumber && c.Downloaded)
                    .Select(c => c.Key)
                    .ToHashSet();

                if (desired.SetEquals(recorded))
                    continue; // bundle is fresh

                if (!inFlight.Add((manga.Key, volume.VolumeNumber)))
                    continue; // already being (re)built

                Log.Info($"Volume {volume.VolumeNumber} of {manga.Name} is stale " +
                         $"(bundled {recorded.Count} chapters, now {desired.Count}); rebuilding.");

                var unbundle = new UnbundleVolumeWorker(manga.Key, volume.VolumeNumber, settings);
                var rebundle = new BundleVolumeWorker(manga.Key, volume.VolumeNumber, settings, dependsOn: [unbundle]);
                jobs.Add(unbundle);
                jobs.Add(rebundle);
            }
        }

        LastExecution = DateTime.UtcNow;
        return jobs.ToArray();
    }
}
