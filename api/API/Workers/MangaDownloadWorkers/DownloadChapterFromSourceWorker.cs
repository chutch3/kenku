using System.Diagnostics.CodeAnalysis;
using API.Acquirers;
using API.JobRuntime;
using API.MangaConnectors;
using API.Services;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Workers.PeriodicWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MangaDownloadWorkers;

/// <summary>
/// Downloads a single chapter for a Series by delegating the actual file-fetch+package step to an
/// IChapterAcquirer (defaults to ImageListAcquirer for the historical image-by-image flow). The
/// worker owns resolving the chapter, persisting download state, library refresh decisions, and
/// cover propagation; the acquirer owns producing the .cbz on disk.
/// </summary>
public class DownloadChapterFromSourceWorker(
    SourceId<Chapter> chId,
    IEnumerable<SeriesSource> connectors,
    KenkuSettings settings,
    IChapterAcquirer? acquirer = null,
    ILibraryLayoutResolver? layoutResolver = null,
    IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn)
{
    public readonly string ChapterIdId = chId.Key;
    private readonly IChapterAcquirer _acquirer = acquirer ?? new ImageListAcquirer(settings);
    private readonly ILibraryLayoutResolver _layoutResolver = layoutResolver ?? new LibraryLayoutResolver();

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private SeriesContext SeriesContext = null!;
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    private ActionsContext ActionsContext = null!;
    private IJobStore _jobStore = null!;
    private IClock _clock = null!;

    protected override void SetContexts(IServiceScope serviceScope)
    {
        SeriesContext = GetContext<SeriesContext>(serviceScope);
        ActionsContext = GetContext<ActionsContext>(serviceScope);
        _jobStore = serviceScope.ServiceProvider.GetRequiredService<IJobStore>();
        _clock = serviceScope.ServiceProvider.GetRequiredService<IClock>();
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        var service = new ChapterDownloadService(settings, connectors, _jobStore, _clock, [_acquirer], _layoutResolver);
        if (!await service.DownloadAsync(SeriesContext, ActionsContext, ChapterIdId, CancellationToken))
            return [];

        bool refreshLibrary = await CheckLibraryRefresh();
        if (refreshLibrary)
            Log.Info($"Condition {settings.LibraryRefreshSetting} met.");
        return refreshLibrary ? [new RefreshLibrariesWorker()] : [];
    }

    private async Task<bool> CheckLibraryRefresh() => settings.LibraryRefreshSetting switch
    {
        LibraryRefreshSetting.AfterAllFinished => await AllDownloadsFinished(),
        LibraryRefreshSetting.AfterMangaFinished => await SeriesContext.MangaConnectorToChapter.Include(chId => chId.Obj).Where(chId => chId.UseForDownload).AllAsync(chId => chId.Obj.Downloaded, CancellationToken),
        LibraryRefreshSetting.AfterEveryChapter => true,
        LibraryRefreshSetting.WhileDownloading => await AllDownloadsFinished() || DateTime.UtcNow.Subtract(RefreshLibrariesWorker.LastRefresh).TotalMinutes > settings.RefreshLibraryWhileDownloadingEveryMinutes,
        _ => true
    };
    private async Task<bool> AllDownloadsFinished() => (await StartNewChapterDownloadsWorker.GetMissingChapters(SeriesContext, CancellationToken)).Count == 0;

    public override string ToString() => $"{base.ToString()} {ChapterIdId}";
}
