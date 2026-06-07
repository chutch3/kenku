using API.Schema.SeriesContext;

namespace API.Services.Interfaces;

/// <summary>Where a chapter's archive ends up on disk under a given <see cref="LibraryLayout"/>.</summary>
public enum ChapterPlacement
{
    /// <summary>Directly in the series directory.</summary>
    SeriesRoot,
    /// <summary>In a "Vol N" subdirectory of the series directory.</summary>
    VolumeFolder
}

/// <summary>
/// The resolved on-disk target for a chapter, plus why it landed there. The reason is surfaced so a
/// user can understand layouts that look "mixed" (e.g. volume-less chapters staying at the root).
/// </summary>
public record ResolvedChapterPath(string FullPath, ChapterPlacement Placement, string Reason);

/// <summary>
/// Resolves where a chapter's archive should be written for a series' <see cref="LibraryLayout"/>.
/// Single source of truth shared by the downloader and the reorganize/preview path.
/// </summary>
public interface ILibraryLayoutResolver
{
    /// <summary>Resolve the target path for <paramref name="chapter"/> under <paramref name="series"/>'s layout.</summary>
    ResolvedChapterPath Resolve(Series series, Chapter chapter, string namingScheme);
}
