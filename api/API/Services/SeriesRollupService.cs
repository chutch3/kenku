using API.Controllers.DTOs;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Builds the per-series operational rollup — actual download progress, live job counts (the resource
/// key is the series for series-scoped jobs), and the most recent failure — the read model the library
/// badge derives its state from.
/// </summary>
public class SeriesRollupService
{
    public async Task<List<SeriesRollup>> GetAsync(SeriesContext series, JobsContext jobsContext,
        ActionsContext actionsContext, CancellationToken ct)
    {
        List<string> seriesKeys = await series.Series.Select(s => s.Key).ToListAsync(ct);

        var chapterStats = await series.Chapters
            .Where(c => c.SourceIds.Any(s => s.UseForDownload))
            .GroupBy(c => c.ParentMangaId)
            .Select(g => new { MangaId = g.Key, Wanted = g.Count(), Downloaded = g.Count(c => c.Downloaded || c.IsBundled) })
            .ToListAsync(ct);

        // The queue is pruned to a retention window, so loading the live rows is bounded.
        var jobs = await jobsContext.JobQueue
            .Where(j => j.ResourceKey != null && j.Status != JobStatus.Succeeded && j.Status != JobStatus.Cancelled)
            .ToListAsync(ct);

        var lastSyncs = await actionsContext.Actions.OfType<ChaptersRetrievedActionRecord>()
            .GroupBy(r => r.MangaId)
            .Select(g => g.OrderByDescending(r => r.PerformedAt).First())
            .ToListAsync(ct);

        return seriesKeys.Select(key =>
        {
            var chapters = chapterStats.FirstOrDefault(c => c.MangaId == key);
            var seriesJobs = jobs.Where(j => j.ResourceKey == key).ToList();
            var sync = lastSyncs.FirstOrDefault(r => r.MangaId == key);
            string? lastError = seriesJobs
                .Where(j => j.Error != null)
                .OrderByDescending(j => j.FinishedAt ?? j.StartedAt ?? j.CreatedAt)
                .Select(j => j.Error)
                .FirstOrDefault();
            return new SeriesRollup(key, chapters?.Wanted ?? 0, chapters?.Downloaded ?? 0,
                seriesJobs.Count(j => j.Status == JobStatus.Queued),
                seriesJobs.Count(j => j.Status == JobStatus.Running),
                seriesJobs.Count(j => j.Status == JobStatus.NeedsAttention),
                lastError, sync?.PerformedAt, sync?.ChapterCount);
        }).ToList();
    }
}
