using API.MangaConnectors;
using API.Schema.NotificationsContext;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>What a Cleanup job targets.</summary>
public enum CleanupKind
{
    /// <summary>Delete already-sent notifications.</summary>
    OldNotifications,
    /// <summary>Delete cover-cache files no series references.</summary>
    MangaCovers,
    /// <summary>Delete source-ids whose connector no longer exists (orphans).</summary>
    OrphanSourceIds
}

/// <summary>
/// The parameterized cleanup domain logic, shared by the Cleanup job handler (and previously the
/// RemoveOldNotifications / CleanupMangaCovers / CleanupSourceIdsWithoutSource workers). Each routine is
/// idempotent: a re-run removes zero new items.
/// </summary>
public class CleanupService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CleanupService));

    public async Task RemoveOldNotificationsAsync(NotificationsContext context, CancellationToken ct)
    {
        Log.Debug("Removing old notifications...");
        int removed = await context.Notifications.Where(n => n.IsSent).ExecuteDeleteAsync(ct);
        Log.DebugFormat("Removed {0} old notifications...", removed);
        if (await context.Sync(ct, typeof(CleanupService), nameof(RemoveOldNotificationsAsync)) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
    }

    public void CleanupMangaCovers(SeriesContext context, KenkuSettings settings, CancellationToken ct)
    {
        Log.Info("Removing stale cover files...");
        string[] usedFiles = context.Series.Where(m => m.CoverFileNameInCache != null)
            .Select(m => m.CoverFileNameInCache!).ToArray();
        foreach (string cache in new[]
                 {
                     settings.CoverImageCacheOriginal, settings.CoverImageCacheLarge,
                     settings.CoverImageCacheMedium, settings.CoverImageCacheSmall
                 })
            DeleteExtraneous(usedFiles, cache);
    }

    private static void DeleteExtraneous(string[] retainFilenames, string imageCachePath)
    {
        DirectoryInfo directory = new(imageCachePath);
        if (!directory.Exists)
            return;
        foreach (string path in directory.GetFiles().Where(f => !retainFilenames.Contains(f.Name)).Select(f => f.FullName))
        {
            Log.InfoFormat("Deleting {0}", path);
            File.Delete(path);
        }
    }

    public async Task CleanupOrphanSourceIdsAsync(SeriesContext context, IEnumerable<SeriesSource> connectors, KenkuSettings settings, CancellationToken ct)
    {
        Log.Info("Cleaning up old connector-data...");
        string[] connectorNames = connectors.Select(c => c.Name).ToArray();
        int deletedChapterIds = await context.MangaConnectorToChapter
            .Where(chId => connectorNames.All(n => n != chId.MangaConnectorName)).ExecuteDeleteAsync(ct);
        Log.InfoFormat("Deleted {0} chapterIds.", deletedChapterIds);

        // Series without a connector are written out before deletion, to not lose data.
        if (await context.MangaConnectorToManga.Include(id => id.Obj)
                .Where(mcId => connectorNames.All(name => name != mcId.MangaConnectorName)).ToListAsync(ct) is { Count: > 0 } list)
        {
            string filePath = Path.Join(settings.WorkingDirectory, $"deletedManga-{DateTime.UtcNow.Ticks}.txt");
            Log.DebugFormat("Writing deleted manga to {0}.", filePath);
            await File.WriteAllLinesAsync(filePath,
                list.Select(id => string.Join('-', id.MangaConnectorName, id.IdOnConnectorSite, id.Obj.Name, id.WebsiteUrl)), ct);
        }
        int deletedMangaIds = await context.MangaConnectorToManga
            .Where(mcId => connectorNames.All(name => name != mcId.MangaConnectorName)).ExecuteDeleteAsync(ct);
        Log.InfoFormat("Deleted {0} mangaIds.", deletedMangaIds);

        if (await context.Sync(ct, typeof(CleanupService), nameof(CleanupOrphanSourceIdsAsync)) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
    }
}
