using API.JobRuntime.Interfaces;
using API.JobRuntime.Handlers;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace API.JobRuntime.Reconcilers;

/// <summary>
/// Periodically enqueues a <see cref="PlaceChapterFileHandler"/> job for every downloaded, unbundled
/// chapter whose stored filename has drifted from the current naming scheme / layout. Replaces the
/// SyncChapterFileNames worker — runs often so a chapter whose volume resolves after download is
/// reconciled promptly instead of CheckDownloaded thrashing its Downloaded flag (#23); deduped per chapter.
/// </summary>
public class ChapterFilePlacementReconciler(IServiceScopeFactory scopeFactory, IClock clock, IConfiguration configuration)
    : Reconciler(scopeFactory, configuration)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    public static string DedupKey(string chapterKey) => $"place-chapter:{chapterKey}";

    protected override Task TickAsync(IServiceProvider scope, CancellationToken ct) =>
        ScanAndEnqueueAsync(
            scope.GetRequiredService<SeriesContext>(),
            scope.GetRequiredService<IJobStore>(),
            scope.GetRequiredService<KenkuSettings>(),
            clock.UtcNow, ct);

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
