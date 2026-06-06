using API.Acquirers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.MangaConnectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Downloads a single chapter: resolve it, fetch+package the .cbz via the <see cref="IChapterAcquirer"/>,
/// mark it Downloaded, propagate the series cover, and enqueue any volume that just became ready to bundle.
/// This is the domain logic shared by the legacy download worker and the DownloadChapter job handler, so
/// both behave identically during the migration. Idempotent: an already-downloaded chapter is a no-op.
/// </summary>
public class ChapterDownloadService(
    KenkuSettings settings,
    IEnumerable<SeriesSource> connectors,
    IJobStore jobStore,
    IClock clock,
    IChapterAcquirer acquirer,
    ILibraryLayoutResolver layoutResolver)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChapterDownloadService));

    /// <summary>Returns true if the chapter was downloaded; false on any skip or failure.</summary>
    public async Task<bool> DownloadAsync(SeriesContext seriesContext, ActionsContext actionsContext, string chapterKey, CancellationToken ct)
    {
        Log.Debug($"Downloading chapter for SourceId {chapterKey}...");
        if (await seriesContext.MangaConnectorToChapter
                .Include(id => id.Obj)
                .ThenInclude(c => c.ParentManga)
                .ThenInclude(m => m.Library)
                .Include(id => id.Obj)
                .ThenInclude(c => c.ParentManga)
                .ThenInclude(m => m.SourceIds) // cover propagation reads ParentManga.SourceIds — must be loaded
                .FirstOrDefaultAsync(c => c.Key == chapterKey, ct) is not { } mangaConnectorId)
        {
            Log.Error("Could not get SourceId.");
            return false;
        }

        if (await mangaConnectorId.Obj.CheckDownloaded(seriesContext, settings.ChapterNamingScheme, token: ct))
        {
            Log.Warn("Chapter already exists!");
            return false;
        }

        SeriesSource? seriesSource = connectors.FirstOrDefault(c => c.Name.Equals(mangaConnectorId.MangaConnectorName, StringComparison.InvariantCultureIgnoreCase));
        if (seriesSource is null)
        {
            Log.Error("Could not get SeriesSource.");
            return false;
        }

        Log.Debug($"Downloading chapter for SourceId {mangaConnectorId}...");

        Chapter chapter = mangaConnectorId.Obj;
        if (chapter.ParentManga.LibraryId is null)
        {
            Log.Info($"Library is not set for {chapter.ParentManga} {chapter}");
            return false;
        }

        // Place the chapter according to the series' LibraryLayout (Flat / Vol N folder) rather than
        // always at the series root. Volume-less chapters fall back to the root (see resolver).
        ResolvedChapterPath target = layoutResolver.Resolve(chapter.ParentManga, chapter, settings.ChapterNamingScheme);
        string saveArchiveFilePath = target.FullPath;
        Log.Debug($"Placing {chapter} at {target.Placement} ({target.Reason}).");
        string? directoryPath = Path.GetDirectoryName(saveArchiveFilePath);
        if (directoryPath != null && !Directory.Exists(directoryPath))
            Directory.CreateDirectory(directoryPath);

        // Delegate the actual fetch + package step. Acquirer logs its own errors and returns null on failure.
        string? acquiredPath = await acquirer.AcquireAsync(mangaConnectorId, seriesSource, saveArchiveFilePath, ct);
        if (acquiredPath is null)
            return false;

        try
        {
            chapter.Downloaded = true;
            // Store the path relative to the series dir so the volume subfolder (if any) is recorded,
            // keeping GetFullFilepath / CheckDownloaded consistent with where the file actually lives.
            chapter.FileName = Path.GetRelativePath(chapter.ParentManga.FullDirectoryPath, acquiredPath);

            Log.Debug($"Downloaded chapter {chapter}.");

            await actionsContext.Actions.AddAsync(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter), ct);

            // Notification emission has moved to NotifyOnNewDownloadsWorker which observes the
            // ChapterDownloadedActionRecord rows produced here — keeps a single emission point that
            // covers both image-list and torrent download paths.
            var syncTasks = new List<Task<(bool success, string? exceptionMessage)>>
            {
                seriesContext.Sync(ct, typeof(ChapterDownloadService), "Download Success"),
                actionsContext.Sync(ct, typeof(ChapterDownloadService), "Download Success")
            };
            foreach (var result in await Task.WhenAll(syncTasks))
                if (!result.success) Log.Error($"Failed to save database changes: {result.exceptionMessage}");

            if (directoryPath != null)
            {
                var sourceIdForSeries = chapter.ParentManga.SourceIds.FirstOrDefault(id => id.MangaConnectorName == seriesSource.Name);
                if (sourceIdForSeries != null)
                    await EnsureCoverInPublicationFolder(seriesContext, chapter.ParentManga, seriesSource, sourceIdForSeries, directoryPath, ct);
            }
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to finalise chapter {0}: {1}", chapter, ex);
            return false; // Fail early!
        }

        await EnqueueReadyVolumeBundleJobs(seriesContext, chapter.ParentManga, ct);
        return true;
    }

    /// <summary>
    /// Under VolumeCBZ, enqueue a ReconcileVolumeBundle job for any volume that just became
    /// closed-and-complete (see <see cref="VolumeBundlePolicy"/>). Volume-less and trailing/in-progress
    /// volumes are skipped, so this no-ops for other layouts and for series still mid-volume. Deduped per
    /// volume so it coalesces with the reconciler.
    /// </summary>
    private async Task EnqueueReadyVolumeBundleJobs(SeriesContext seriesContext, Series manga, CancellationToken ct)
    {
        if (manga.LibraryLayout != LibraryLayout.VolumeCBZ)
            return;

        await seriesContext.Entry(manga).Collection(m => m.Chapters).LoadAsync(ct);

        foreach (int volume in VolumeBundlePolicy.VolumesReadyToBundle(manga))
            await jobStore.EnqueueAsync(new Job(ReconcileVolumeBundleHandler.Type,
                ReconcileVolumeBundleHandler.PayloadFor(manga.Key, volume), clock.UtcNow,
                resourceKey: manga.Key, dedupKey: VolumeBundleReconciler.DedupKey(manga.Key, volume)), ct);
    }

    private async Task EnsureCoverInPublicationFolder(SeriesContext seriesContext, Series manga, SeriesSource seriesSource, SourceId<Series> mangaConnectorId, string publicationFolder, CancellationToken ct)
    {
        if (File.Exists(Path.Join(publicationFolder, "cover.jpg"))) return;

        string? coverFileNameInCache = manga.CoverFileNameInCache;
        if (coverFileNameInCache is null)
        {
            Log.Debug("Cover filename in cache is null. Attempting to download...");
            coverFileNameInCache = await seriesSource.SaveCoverImageToCache(mangaConnectorId);
            manga.CoverFileNameInCache = coverFileNameInCache;
            if (await seriesContext.Sync(ct, reason: "Update cover filename") is { success: false } result)
                Log.Error($"Couldn't update cover filename {result.exceptionMessage}");
        }

        if (coverFileNameInCache is null)
        {
            Log.Error("Could not retrieve cover image cache filename.");
            return;
        }

        string fullCoverPath = Path.Join(settings.CoverImageCacheOriginal, coverFileNameInCache);
        if (!File.Exists(fullCoverPath))
        {
            Log.Error($"Cached cover file {fullCoverPath} does not exist.");
            return;
        }

        string extension = Path.GetExtension(coverFileNameInCache);
        string newFilePath = Path.Join(publicationFolder, $"cover{extension}");
        File.Copy(fullCoverPath, newFilePath, true);
        Log.Debug($"Copied cover from {fullCoverPath} to {newFilePath}");
    }
}
