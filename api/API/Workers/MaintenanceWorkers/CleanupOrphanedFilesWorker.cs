using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;

namespace API.Workers.MaintenanceWorkers;

/// <summary>
/// Deletes archive files in a library that are not tracked as downloaded chapters in the database.
/// </summary>
/// <remarks>
/// This worker is destructive, so it ships with safety rails:
/// <list type="bullet">
/// <item>The automatically scheduled instance runs in <c>dryRun</c> mode — it only reports orphans
/// and never deletes. Deletion must be requested explicitly via the Maintenance API.</item>
/// <item>Even with <c>dryRun=false</c>, a library is skipped when none of its on-disk archives are
/// tracked (the classic "library path is wrong / series not imported yet" case that would otherwise
/// wipe the whole library) or when orphans exceed <see cref="MaxDeleteFraction"/> of the library.
/// Pass <c>force=true</c> to override these guards for a deliberate bulk cleanup.</item>
/// </list>
/// </remarks>
public class CleanupOrphanedFilesWorker(bool dryRun = false, bool force = false, IEnumerable<BaseWorker>? dependsOn = null)
    : BaseWorkerWithContexts(dependsOn), IPeriodic
{
    private SeriesContext _mangaContext = null!;
    private readonly bool _dryRun = dryRun;
    private readonly bool _force = force;

    /// <summary>
    /// Refuse to auto-delete more than this fraction of a single library's archives without <c>force</c>.
    /// Guards against a partially-populated database silently nuking most of a library.
    /// </summary>
    private const double MaxDeleteFraction = 0.5;

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cbz", ".zip", ".rar" };

    // IPeriodic implementation
    public DateTime LastExecution { get; set; } = DateTime.MinValue;
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    protected override void SetContexts(IServiceScope serviceScope)
    {
        _mangaContext = GetContext<SeriesContext>(serviceScope);
    }

    protected override async Task<BaseWorker[]> DoWorkInternal()
    {
        Log.Info($"Starting Orphaned Files Cleanup (DryRun: {_dryRun}, Force: {_force})...");

        // 1. Get all libraries
        List<FileLibrary> libraries = await _mangaContext.FileLibraries.ToListAsync(CancellationToken);

        // 2. Get all chapters that are downloaded and their full paths
        List<Chapter> chapters = await _mangaContext.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => c.Downloaded && c.FileName != null)
            .ToListAsync(CancellationToken);

        HashSet<string> trackedPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Chapter chapter in chapters)
        {
            if (chapter.GetFullFilepath(null) is { } path)
            {
                trackedPaths.Add(Path.GetFullPath(path));
            }
        }

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

            // Guard 1: archives exist on disk but none are tracked. This is almost always a
            // misconfigured library path or a series that hasn't been imported yet — NOT a library
            // full of genuine orphans. Refuse to delete the whole thing.
            if (!_force && trackedHere == 0)
            {
                Log.Warn($"Skipping cleanup of '{library.LibraryName}' ({library.BasePath}): " +
                         $"{archives.Count} archive(s) on disk but 0 tracked in the database. " +
                         "Refusing to wipe an untracked library. Re-import the series first, or pass force=true to override.");
                continue;
            }

            // Guard 2: a majority of the library looks orphaned. Likely a partially-populated DB
            // rather than reality. Bail out instead of deleting most of the library.
            if (!_force && orphans.Count > archives.Count * MaxDeleteFraction)
            {
                Log.Warn($"Skipping cleanup of '{library.LibraryName}' ({library.BasePath}): " +
                         $"{orphans.Count}/{archives.Count} archives appear orphaned (> {MaxDeleteFraction:P0}). " +
                         "Refusing as a safety measure. Pass force=true to override.");
                continue;
            }

            foreach (string fullPath in orphans)
            {
                FileInfo fileInfo = new(fullPath);
                Log.Info($"{(_dryRun ? "[DRY RUN] Would delete" : "Deleting")} orphaned file: {fullPath} ({fileInfo.Length / 1024.0 / 1024.0:F2} MB)");

                if (!_dryRun)
                {
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
                else
                {
                    deletedCount++;
                    deletedSize += fileInfo.Length;
                }
            }
        }

        Log.Info($"Cleanup complete. {(_dryRun ? "Found" : "Deleted")} {deletedCount} files ({deletedSize / 1024.0 / 1024.0:F2} MB).");

        LastExecution = DateTime.UtcNow;
        return [];
    }

    public override string ToString() => $"{base.ToString()} DryRun={_dryRun} Force={_force}";
}
