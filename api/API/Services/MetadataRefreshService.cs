using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Refreshes one series' external metadata: runs each of its <see cref="MetadataEntry"/>'s fetcher and
/// records the update. Shared by the RefreshExternalMetadata job handler (formerly UpdateMetadataWorker,
/// which did this in bulk for every tracked series).
/// </summary>
public class MetadataRefreshService(IEnumerable<MetadataFetcher> metadataFetchers)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(MetadataRefreshService));

    public async Task RefreshAsync(SeriesContext seriesContext, ActionsContext actionsContext, string mangaId, CancellationToken ct)
    {
        List<MetadataEntry> entries = await seriesContext.MetadataEntries
            .Include(e => e.Series)
            .Where(e => e.MangaId == mangaId)
            .ToListAsync(ct);
        Log.DebugFormat("Updating metadata for {0} ({1} entries)...", mangaId, entries.Count);

        foreach (MetadataEntry entry in entries)
        {
            if (metadataFetchers.FirstOrDefault(f => f.Name == entry.MetadataFetcherName) is not { } fetcher)
                continue;
            await fetcher.UpdateMetadata(entry, seriesContext, ct);
            actionsContext.Actions.Add(new MetadataUpdatedActionRecord(entry.Series, fetcher));
        }

        if (await seriesContext.Sync(ct, typeof(MetadataRefreshService), nameof(RefreshAsync)) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
        if (await actionsContext.Sync(ct, typeof(MetadataRefreshService), "Metadata Updated") is { success: false } ae)
            Log.ErrorFormat("Failed to save database changes: {0}", ae.exceptionMessage);
    }
}
