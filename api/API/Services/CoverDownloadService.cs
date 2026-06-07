using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Downloads and caches the cover image for a series source and records a CoverDownloadedActionRecord —
/// the logic formerly in DownloadCoverFromSourceWorker. Idempotent: SaveCoverImageToCache no-ops when the
/// file is already cached.
/// </summary>
public class CoverDownloadService(IEnumerable<SeriesSource> connectors)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CoverDownloadService));

    public async Task DownloadAsync(SeriesContext seriesContext, ActionsContext actionsContext, string sourceIdKey, CancellationToken ct)
    {
        if (await seriesContext.MangaConnectorToManga
                .Include(id => id.Obj)
                .FirstOrDefaultAsync(c => c.Key == sourceIdKey, ct) is not { } mangaConnectorId)
        {
            Log.Error($"Could not get SourceId {sourceIdKey}.");
            return;
        }

        SeriesSource? seriesSource = connectors.FirstOrDefault(c =>
            c.Name.Equals(mangaConnectorId.MangaConnectorName, StringComparison.InvariantCultureIgnoreCase));
        if (seriesSource is null)
        {
            Log.Error($"Could not get SeriesSource for {mangaConnectorId.MangaConnectorName}.");
            return;
        }

        string? coverFileName = await seriesSource.SaveCoverImageToCache(mangaConnectorId);
        if (coverFileName is null)
        {
            Log.Error($"Could not get Cover for SourceId {sourceIdKey}.");
            return;
        }

        mangaConnectorId.Obj.CoverFileNameInCache = coverFileName;
        if (await seriesContext.Sync(ct, typeof(CoverDownloadService), nameof(DownloadAsync)) is { success: false } seriesError)
            Log.Error($"Failed to save database changes: {seriesError.exceptionMessage}");

        actionsContext.Actions.Add(new CoverDownloadedActionRecord(mangaConnectorId.Obj, coverFileName));
        if (await actionsContext.Sync(ct, typeof(CoverDownloadService), nameof(DownloadAsync)) is { success: false } actionsError)
            Log.Error($"Failed to save database changes: {actionsError.exceptionMessage}");
    }
}
