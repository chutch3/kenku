using API.Schema.SeriesContext;
using API.Services;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

public class UnbundleVolumeWorker(string mangaId, int volumeNumber, KenkuSettings settings, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    /// <summary><see cref="Series"/>.Key of the manga whose volume is being unbundled.</summary>
    public string MangaId => mangaId;
    /// <summary>The volume number being unbundled back into individual chapters.</summary>
    public int VolumeNumber => volumeNumber;

    private SeriesContext _mangaContext = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        await new VolumeBundler(settings).UnbundleAsync(_mangaContext, mangaId, volumeNumber, CancellationToken);
        return [];
    }

    public override string ToString() => $"{base.ToString()} manga={mangaId} vol={volumeNumber}";
}
