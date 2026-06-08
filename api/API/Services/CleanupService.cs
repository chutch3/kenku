using API.Connectors;
using API.Schema.JobsContext;
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
    OrphanSourceIds,
    /// <summary>Delete library archive files not tracked as downloaded chapters.</summary>
    OrphanedFiles,
    /// <summary>Delete completed (Succeeded/Cancelled) jobs older than the retention window.</summary>
    CompletedJobs
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

    /// <summary>Prunes completed (Succeeded/Cancelled) jobs whose FinishedAt is older than the retention
    /// window — they are history nobody revisits. Failed/NeedsAttention are kept regardless of age so they
    /// stay visible for action.</summary>
    public async Task CleanupCompletedJobsAsync(JobsContext context, DateTime now, TimeSpan retention, CancellationToken ct)
    {
        DateTime cutoff = now - retention;
        int removed = await context.JobQueue
            .Where(j => (j.Status == JobStatus.Succeeded || j.Status == JobStatus.Cancelled)
                        && j.FinishedAt != null && j.FinishedAt < cutoff)
            .ExecuteDeleteAsync(ct);
        Log.DebugFormat("Removed {0} completed jobs finished before {1:o}.", removed, cutoff);
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

    /// <summary>Refuse to auto-delete more than this fraction of a library's archives without <c>force</c>.</summary>
    private const double MaxDeleteFraction = 0.5;

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip", ".rar" };

    /// <summary>
    /// Deletes archive files in each library that aren't tracked as downloaded chapters. Destructive, so:
    /// <c>dryRun</c> only reports; and without <c>force</c> a library is skipped if none of its archives are
    /// tracked (misconfigured path) or if a majority look orphaned (partial DB) — guarding against wipes.
    /// </summary>
    public async Task CleanupOrphanedFilesAsync(SeriesContext context, bool dryRun, bool force, CancellationToken ct)
    {
        Log.Info($"Starting Orphaned Files Cleanup (DryRun: {dryRun}, Force: {force})...");

        List<FileLibrary> libraries = await context.FileLibraries.ToListAsync(ct);
        List<Chapter> chapters = await context.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => c.Downloaded && c.FileName != null)
            .ToListAsync(ct);

        HashSet<string> trackedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Chapter chapter in chapters)
            if (chapter.GetFullFilepath(null) is { } path)
                trackedPaths.Add(Path.GetFullPath(path));
        Log.Debug($"Found {trackedPaths.Count} tracked files in database.");

        int deletedCount = 0;
        long deletedSize = 0;

        foreach (FileLibrary library in libraries)
        {
            if (!Directory.Exists(library.BasePath))
            {
                Log.Warn($"Library path does not exist: {library.BasePath}");
                continue;
            }

            Log.Debug($"Scanning library: {library.LibraryName} ({library.BasePath})");
            List<string> archives = Directory.GetFiles(library.BasePath, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(p => ArchiveExtensions.Contains(Path.GetExtension(p)))
                .ToList();
            if (archives.Count == 0)
                continue;

            List<string> orphans = archives.Where(p => !trackedPaths.Contains(p)).ToList();
            int trackedHere = archives.Count - orphans.Count;

            // Guard 1: on-disk archives but none tracked → misconfigured path / un-imported series, not orphans.
            if (!force && trackedHere == 0)
            {
                Log.Warn($"Skipping cleanup of '{library.LibraryName}' ({library.BasePath}): " +
                         $"{archives.Count} archive(s) on disk but 0 tracked. Refusing to wipe an untracked library (force=true to override).");
                continue;
            }

            // Guard 2: a majority looks orphaned → likely a partial DB, not reality.
            if (!force && orphans.Count > archives.Count * MaxDeleteFraction)
            {
                Log.Warn($"Skipping cleanup of '{library.LibraryName}' ({library.BasePath}): " +
                         $"{orphans.Count}/{archives.Count} archives appear orphaned (> {MaxDeleteFraction:P0}). Refusing (force=true to override).");
                continue;
            }

            foreach (string fullPath in orphans)
            {
                FileInfo fileInfo = new(fullPath);
                Log.Info($"{(dryRun ? "[DRY RUN] Would delete" : "Deleting")} orphaned file: {fullPath} ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");
                if (dryRun)
                {
                    deletedCount++;
                    deletedSize += fileInfo.Length;
                    continue;
                }
                try
                {
                    File.Delete(fullPath);
                    deletedCount++;
                    deletedSize += fileInfo.Length;
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to delete {fullPath}: {ex.Message}");
                }
            }
        }

        Log.Info($"Cleanup complete. {(dryRun ? "Found" : "Deleted")} {deletedCount} files ({deletedSize / 1024.0 / 1024.0:F2} MB).");
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
