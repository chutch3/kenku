using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Places a downloaded chapter's archive at its layout-correct path: computes the expected filename
/// (layout + naming scheme) when none is given, moves the file if it has drifted, and records the new
/// name. Shared by the PlaceChapterFile job handler and the reorganize endpoint — replacing the
/// RenameChapterFile / SyncChapterFileNames workers. Idempotent: a chapter already at its expected
/// path is a no-op.
/// </summary>
public class ChapterFilePlacementService(KenkuSettings settings, ILibraryLayoutResolver? layoutResolver = null)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ChapterFilePlacementService));
    private readonly ILibraryLayoutResolver _layoutResolver = layoutResolver ?? new LibraryLayoutResolver();

    /// <summary>The layout-correct filename (relative to the series directory) for a chapter.</summary>
    public string ExpectedFileName(Series manga, Chapter chapter) =>
        Path.GetRelativePath(manga.FullDirectoryPath,
            _layoutResolver.Resolve(manga, chapter, settings.ChapterNamingScheme).FullPath);

    /// <summary>
    /// Moves the chapter's archive to <paramref name="targetFileName"/> (or its layout-correct name when
    /// null) and updates <see cref="Chapter.FileName"/>. A failed file move still updates the DB name, so a
    /// later reconciliation pass converges instead of thrashing.
    /// </summary>
    public async Task PlaceAsync(SeriesContext context, string chapterKey, string? targetFileName, CancellationToken ct)
    {
        var chapter = await context.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .FirstOrDefaultAsync(c => c.Key == chapterKey, ct);

        if (chapter is null) return;

        string newFileName = targetFileName ?? ExpectedFileName(chapter.ParentManga, chapter);
        if (chapter.FileName == newFileName) return;

        string? oldPath = chapter.FullArchiveFilePath;
        string? newPath = chapter.ParentManga.FullDirectoryPath is { } dir
            ? Path.Join(dir, newFileName)
            : null;

        if (oldPath != null && newPath != null && oldPath != newPath && File.Exists(oldPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            try
            {
                File.Move(oldPath, newPath);
            }
            catch (IOException ex)
            {
                Log.Warn($"Could not move '{oldPath}' to '{newPath}': {ex.Message}. Updating DB filename anyway.");
            }
        }

        chapter.FileName = newFileName;
        await context.Sync(ct, typeof(ChapterFilePlacementService), nameof(PlaceAsync));
    }
}
