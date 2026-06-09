using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>How a cover refresh ended; the job records this so a missing cover is explainable.</summary>
public enum CoverOutcome
{
    Cached,
    /// <summary>The source has no cover URL — linking MyAnimeList/Metron backfills one.</summary>
    NoCoverUrl,
    SourceMissing,
    FetchFailed
}

/// <summary>
/// Downloads and caches the cover image for a series source and records a CoverDownloadedActionRecord —
/// the logic formerly in DownloadCoverFromSourceWorker. Idempotent: SaveCoverImageToCache no-ops when the
/// file is already cached.
/// </summary>
public class CoverDownloadService(IEnumerable<SeriesSource> connectors)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CoverDownloadService));

    public async Task<CoverOutcome> DownloadAsync(SeriesContext seriesContext, ActionsContext actionsContext, string sourceIdKey, CancellationToken ct)
    {
        if (await seriesContext.MangaConnectorToManga
                .Include(id => id.Obj)
                .FirstOrDefaultAsync(c => c.Key == sourceIdKey, ct) is not { } mangaConnectorId)
        {
            Log.Error($"Could not get SourceId {sourceIdKey}.");
            return CoverOutcome.SourceMissing;
        }

        // Indexer/torrent-sourced series arrive with no cover URL; there is nothing to fetch and the
        // download path would dereference the empty URL. Skip cleanly instead of failing the job.
        if (string.IsNullOrWhiteSpace(mangaConnectorId.Obj.CoverUrl))
        {
            Log.DebugFormat("No cover URL for {0}; skipping cover download.", sourceIdKey);
            return CoverOutcome.NoCoverUrl;
        }

        SeriesSource? seriesSource = connectors.FirstOrDefault(c =>
            c.Name.Equals(mangaConnectorId.MangaConnectorName, StringComparison.InvariantCultureIgnoreCase));
        if (seriesSource is null)
        {
            Log.Error($"Could not get SeriesSource for {mangaConnectorId.MangaConnectorName}.");
            return CoverOutcome.SourceMissing;
        }

        string? coverFileName = await seriesSource.SaveCoverImageToCache(mangaConnectorId);
        if (coverFileName is null)
        {
            Log.Error($"Could not get Cover for SourceId {sourceIdKey}.");
            return CoverOutcome.FetchFailed;
        }

        mangaConnectorId.Obj.CoverFileNameInCache = coverFileName;
        if (await seriesContext.Sync(ct, typeof(CoverDownloadService), nameof(DownloadAsync)) is { success: false } seriesError)
            Log.Error($"Failed to save database changes: {seriesError.exceptionMessage}");

        actionsContext.Actions.Add(new CoverDownloadedActionRecord(mangaConnectorId.Obj, coverFileName));
        if (await actionsContext.Sync(ct, typeof(CoverDownloadService), nameof(DownloadAsync)) is { success: false } actionsError)
            Log.Error($"Failed to save database changes: {actionsError.exceptionMessage}");

        return CoverOutcome.Cached;
    }
}
