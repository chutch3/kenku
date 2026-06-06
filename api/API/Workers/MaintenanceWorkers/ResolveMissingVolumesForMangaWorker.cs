using System.Collections.Concurrent;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

public class ResolveMissingVolumesForMangaWorker(
    ConcurrentQueue<string> queue,
    KenkuSettings settings,
    IMangaDexVolumeResolver mangaDexVolumeResolver,
    IMangaDexSearchService mangaDexSearchService,
    IEnumerable<IVolumeResolver>? volumeResolvers = null,
    IEnumerable<BaseWorker>? dependsOn = null)
    : PoolWorker<string>(queue, dependsOn)
{
    private SeriesContext _mangaContext = null!;
    private readonly VolumeResolutionService _resolver =
        new(settings, mangaDexVolumeResolver, mangaDexSearchService, volumeResolvers);

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<IEnumerable<BaseWorker>> ProcessItem(string mangaId)
    {
        await _resolver.ResolveAsync(_mangaContext, mangaId, CancellationToken);
        return [];
    }
}
