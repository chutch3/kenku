using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace API.Workers.MaintenanceWorkers;

public class SyncChapterFileNamesWorker(KenkuSettings settings, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    private SeriesContext _mangaContext = null!;
    private readonly ILibraryLayoutResolver _layoutResolver = new LibraryLayoutResolver();

    public DateTime LastExecution { get; set; } = DateTime.MinValue;
    // Runs often so a chapter whose volume was resolved after download is reconciled promptly,
    // instead of CheckDownloaded thrashing its Downloaded flag until the next daily run (#23).
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        var chapters = await _mangaContext.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => c.Downloaded && c.FileName != null && !c.IsBundled)
            .ToListAsync(CancellationToken);

        List<BaseWorker> newJobs = new();

        foreach (var chapter in chapters)
        {
            // Layout-aware target (Vol N/ folder + naming scheme), so reconciliation agrees with where
            // the downloader places files instead of flattening them back out of their volume folder.
            string expected = Path.GetRelativePath(
                chapter.ParentManga.FullDirectoryPath,
                _layoutResolver.Resolve(chapter.ParentManga, chapter, settings.ChapterNamingScheme).FullPath);
            if (chapter.FileName == expected) continue;
            newJobs.Add(new RenameChapterFileWorker(chapter.Key, expected, settings));
        }

        LastExecution = DateTime.UtcNow;
        return newJobs.ToArray();
    }
}
