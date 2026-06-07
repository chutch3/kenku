using API.Services.Interfaces;
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
            LibraryLayout.VolumeFolder when volumeNumber is { } volume =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, $"Vol {volume}", archiveFileName),
                    ChapterPlacement.VolumeFolder,
                    $"volume {volume}"),
            LibraryLayout.VolumeFolder =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, archiveFileName),
                    ChapterPlacement.SeriesRoot,
                    "no volume number; placed at series root"),
            // VolumeCBZ chapters are merged into a single Vol N.cbz, so they stay flat at the series
            // root pre-bundle — an intermediate "Vol N/" folder would just be a stray series in Komga.
            LibraryLayout.VolumeCBZ =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, archiveFileName),
                    ChapterPlacement.SeriesRoot,
                    "volume-cbz: flat until bundled into Vol N.cbz"),
            _ =>
                new ResolvedChapterPath(
                    Path.Join(baseDirectory, archiveFileName),
                    ChapterPlacement.SeriesRoot,
                    "flat layout")
        };
}
