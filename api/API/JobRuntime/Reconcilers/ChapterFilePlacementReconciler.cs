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
/// Periodically enqueues a <see cref="PlaceChapterFileHandler"/> job for every downloaded, unbundled
/// chapter whose stored filename has drifted from the current naming scheme / layout. Replaces the
/// SyncChapterFileNames worker — runs often so a chapter whose volume resolves after download is
/// reconciled promptly instead of CheckDownloaded thrashing its Downloaded flag (#23); deduped per chapter.
/// </summary>
public class ChapterFilePlacementReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : BackgroundService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChapterFilePlacementReconciler));
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    public static string DedupKey(string chapterKey) => $"place-chapter:{chapterKey}";

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
                    scope.ServiceProvider.GetRequiredService<KenkuSettings>(),
                    clock.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Error("Chapter file placement reconciler error", e); }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Enqueues a placement job for each downloaded, unbundled chapter whose filename has drifted, deduped.</summary>
    public static async Task<int> ScanAndEnqueueAsync(SeriesContext context, IJobStore store, KenkuSettings settings,
        DateTime now, CancellationToken ct)
    {
        var chapters = await context.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => c.Downloaded && c.FileName != null && !c.IsBundled)
            .ToListAsync(ct);

        var placement = new ChapterFilePlacementService(settings);
        int enqueued = 0;
        foreach (var chapter in chapters)
        {
            if (chapter.FileName == placement.ExpectedFileName(chapter.ParentManga, chapter)) continue;
            await store.EnqueueAsync(new Job(PlaceChapterFileHandler.Type,
                PlaceChapterFileHandler.PayloadFor(chapter.Key), now,
                resourceKey: chapter.ParentManga.Key, dedupKey: DedupKey(chapter.Key)), ct);
            enqueued++;
        }
        return enqueued;
    }
}
