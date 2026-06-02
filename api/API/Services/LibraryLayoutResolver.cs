using API.Schema.SeriesContext;

namespace API.Services;

/// <inheritdoc cref="ILibraryLayoutResolver"/>
public class LibraryLayoutResolver : ILibraryLayoutResolver
{
    public ResolvedChapterPath Resolve(Series series, Chapter chapter, string namingScheme)
        => ComputePath(series.LibraryLayout, series.FullDirectoryPath, chapter.VolumeNumber,
            chapter.GetArchiveFileName(namingScheme));

    /// <summary>
    /// Pure layout→path computation. VolumeFolder and VolumeCBZ both place numbered chapters in a
    /// "Vol N" subfolder (VolumeCBZ chapters live there until they are bundled); chapters without a
    /// volume number stay at the series root regardless of layout, since there is nothing to group
    /// them under.
    /// </summary>
    public static ResolvedChapterPath ComputePath(LibraryLayout layout, string baseDirectory, int? volumeNumber, string archiveFileName)
        => layout switch
        {
            LibraryLayout.VolumeFolder or LibraryLayout.VolumeCBZ when volumeNumber is { } volume =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, $"Vol {volume}", archiveFileName),
                    ChapterPlacement.VolumeFolder,
                    $"volume {volume}"),
            LibraryLayout.VolumeFolder or LibraryLayout.VolumeCBZ =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, archiveFileName),
                    ChapterPlacement.SeriesRoot,
                    "no volume number; placed at series root"),
            _ =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, archiveFileName),
                    ChapterPlacement.SeriesRoot,
                    "flat layout")
        };
}
