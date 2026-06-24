using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Syncs a series' chapter list from its connector: fetches available chapters, adds the new ones,
/// backfills volume numbers on existing chapters, and marks new chapters for download if the series is
/// tracked. Shared by the legacy retrieve worker and the SyncSeriesChapters job handler. Additive only —
/// a connector parse miss adds nothing and never deletes local chapters (§4.1).
/// </summary>
public class SeriesChapterSyncService(IEnumerable<SeriesSource> connectors)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(SeriesChapterSyncService));

    /// <summary>Returns (reported, added): how many chapters the connector listed, and how many were new.</summary>
    public async Task<(int reported, int added)> SyncAsync(SeriesContext seriesContext, ActionsContext actionsContext, string sourceIdKey, string language, CancellationToken ct)
    {
        Log.DebugFormat("Getting Chapters for SourceId {0}...", sourceIdKey);
        // Hard failures throw so the dispatcher records them and bounded retry → NeedsAttention applies.
        // Swallowing them here left sync jobs "Succeeded" while the series sat empty with no signal.
        if (await seriesContext.SeriesSourceIds
                .Include(id => id.Obj)
                .ThenInclude(m => m.Chapters)
                .ThenInclude(ch => ch.SourceIds)
                .FirstOrDefaultAsync(c => c.Key == sourceIdKey, ct) is not { } mangaConnectorId)
            throw new InvalidOperationException($"SourceId '{sourceIdKey}' not found.");

        SeriesSource? seriesSource = connectors.FirstOrDefault(c => c.Name.Equals(mangaConnectorId.SeriesSourceName, StringComparison.InvariantCultureIgnoreCase));
        if (seriesSource is null)
            throw new InvalidOperationException($"SeriesSource '{mangaConnectorId.SeriesSourceName}' is not registered.");
        Log.DebugFormat("Getting Chapters for SourceId {0}...", mangaConnectorId);

        Series manga = mangaConnectorId.Obj;

        // Source cover URLs rot (rotating CDN hosts), which breaks both the UI hotlink and the cover
        // cache refresh forever. Re-resolving here keeps the stored URL fetchable. Never fails the
        // sync — chapters matter more than covers.
        try
        {
            if (await seriesSource.GetSeriesFromId(mangaConnectorId.IdOnConnectorSite) is ({ } fresh, _)
                && manga.SetCover(fresh.CoverUrl, CoverSource.Connector))
                Log.InfoFormat("Cover URL for {0} changed; updating from the connector.", manga.Name);
        }
        catch (Exception e)
        {
            Log.WarnFormat("Could not refresh the cover URL for {0}: {1}", manga.Name, e.Message);
        }

        // Retrieve available Chapters from Connector.
        (Chapter chapter, SourceId<Chapter> chapterId)[] fetched = await seriesSource.GetChapters(mangaConnectorId, language);

        // Self-heal chapters that pre-date scan-group capture: backfill missing group/language and
        // re-resolve titles on existing chapters from the full upload set — done before the reconcile
        // below collapses uploads and mutates their titles.
        ChapterSyncBackfill.Apply(manga.Chapters, fetched);

        // Collapse the several uploads a connector may return for one chapter number into one chapter
        // (clearing the title when they disagree).
        // Then drop uploads that resolve to the same source-id key (connector + id-on-site): a connector
        // can list one upload under two chapters (e.g. ComicHubFree's "tpb"/"full" one-shots), and since
        // each chapter carries its own source-id those would cascade-insert duplicate PK_ChapterSourceIds.
        (Chapter chapter, SourceId<Chapter> chapterId)[] allChapters = ChapterTitleReconciler.Reconcile(fetched)
            .DistinctBy(c => c.Item2.Key)
            .ToArray();
        Log.DebugFormat("Got {0} chapters from connector.", allChapters.Length);

        // Filter for new Chapters
        List<(Chapter chapter, SourceId<Chapter> chapterId)> newChapters = allChapters.Where<(Chapter chapter, SourceId<Chapter> chapterId)>(ch =>
            manga.Chapters.All(c => c.Key != ch.chapter.Key)).ToList();
        Log.DebugFormat("Got {0} new chapters.", newChapters.Count);

        // Update existing chapters with metadata if it was missing
        foreach (var (fetchedChapter, _) in allChapters)
        {
            var existingChapter = manga.Chapters.FirstOrDefault(c => c.Key == fetchedChapter.Key);
            if (existingChapter != null && existingChapter.VolumeNumber == null && fetchedChapter.VolumeNumber != null)
            {
                existingChapter.VolumeNumber = fetchedChapter.VolumeNumber;
                Log.DebugFormat("Updated volume for existing chapter {0} to {1}", existingChapter.ChapterNumber, existingChapter.VolumeNumber);
            }
        }

        // Add Chapters to Series
        manga.Chapters = manga.Chapters.Union(newChapters.Select(ch => ch.chapter)).ToList();

        // Filter for new ChapterIds
        List<SourceId<Chapter>> existingChapterIds = manga.Chapters.SelectMany(c => c.SourceIds).ToList();
        List<SourceId<Chapter>> newIds = allChapters.Select(ch => ch.chapterId)
            .Where(newCh => !existingChapterIds.Any(existing =>
                existing.SeriesSourceName == newCh.SeriesSourceName &&
                existing.IdOnConnectorSite == newCh.IdOnConnectorSite))
            .ToList();
        // Match tracked entities of Chapters
        foreach (SourceId<Chapter> newId in newIds)
            newId.Obj = manga.Chapters.First(ch => ch.Key == newId.ObjId);
        Log.DebugFormat("Got {0} new download-Ids.", newIds.Count);

        // Add new ChapterIds to Database
        seriesContext.ChapterSourceIds.AddRange(newIds);

        // If Series is marked for Download from Connector, mark the new Chapters as UseForDownload
        if (mangaConnectorId.UseForDownload)
        {
            foreach ((Chapter _, SourceId<Chapter> chapterId) in newChapters)
                chapterId.UseForDownload = mangaConnectorId.UseForDownload;
        }

        // Throw, don't swallow: a failed save (e.g. a colliding chapter source-id) must fail the job so it
        // surfaces as NeedsAttention instead of "Succeeded" over a series that persisted nothing.
        // Throw, don't swallow: a failed save (e.g. a colliding chapter source-id) must fail the job so it
        // surfaces as NeedsAttention instead of "Succeeded" over a series that persisted nothing.
        if (await seriesContext.Sync(ct, typeof(SeriesChapterSyncService), "Chapters retrieved") is { success: false } mangaContextException)
            throw new InvalidOperationException($"Failed to save chapters for {manga.Name}: {mangaContextException.exceptionMessage}");

        actionsContext.Actions.Add(new ChaptersRetrievedActionRecord(manga, allChapters.Length));
        if (await actionsContext.Sync(ct, typeof(SeriesChapterSyncService), "Chapters retrieved") is { success: false } actionsContextException)
            Log.ErrorFormat("Failed to save database changes: {0}", actionsContextException.exceptionMessage);

        return (allChapters.Length, newChapters.Count);
    }
}
