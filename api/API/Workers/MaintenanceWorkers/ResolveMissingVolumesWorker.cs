using System.Collections.Concurrent;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

public class ResolveMissingVolumesWorker(KenkuSettings settings, IBatchWorkerFactory<string> factory, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    private SeriesContext _mangaContext = null!;

    public DateTime LastExecution { get; set; } = DateTime.MinValue;
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        if (settings.VolumeResolutionStrategy == VolumeResolutionStrategy.Disabled)
        {
            Log.Info("Volume resolution is disabled in settings. Skipping.");
            return [];
        }

        // Any chapter missing a volume is a candidate — exact sources can place undownloaded chapters,
        // so we don't gate on the .cbz being present (that gate is only for the color heuristic itself).
        var mangaIds = await _mangaContext.Chapters
            .Where(c => c.VolumeNumber == null)
            .Select(c => c.ParentMangaId)
            .Distinct()
            .ToListAsync(CancellationToken);

        if (mangaIds.Count == 0)
        {
            Log.Info("No chapters are missing volume numbers.");
            LastExecution = DateTime.UtcNow;
            return [];
        }

        Log.Info($"Found {mangaIds.Count} manga with chapters missing volumes. Spawning workers...");
        var queue = new ConcurrentQueue<string>(mangaIds);
        int n = Math.Max(1, Math.Min(settings.VolumeResolutionParallelism, mangaIds.Count));
        return Enumerable.Range(0, n).Select(_ => factory.Create(queue)).ToArray<BaseWorker>();
    }

    public override string ToString() => $"{base.ToString()} Strategy={settings.VolumeResolutionStrategy}";
}
