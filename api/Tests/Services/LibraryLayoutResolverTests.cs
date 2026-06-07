using API.Services.Interfaces;
using API;
using API.Schema.SeriesContext;
using API.Services;
using SchemaManga = API.Schema.SeriesContext.Series;
using SchemaFileLibrary = API.Schema.SeriesContext.FileLibrary;
using SchemaChapter = API.Schema.SeriesContext.Chapter;

namespace API.Tests.Services;

/// <summary>
/// Unit tests for the layout→path logic that both the downloader and the reorganize/preview path
/// rely on. Kept as pure string computation so it can be asserted without EF or the filesystem.
/// </summary>
public class LibraryLayoutResolverTests
{
    private const string BaseDir = "/library/My Series";
    private const string FileName = "My Series - Ch.1.cbz";

    [Fact]
    public void Flat_PlacesAtSeriesRoot_RegardlessOfVolume()
    {
        var withVolume = LibraryLayoutResolver.ComputePath(LibraryLayout.Flat, BaseDir, 1, FileName);
        var withoutVolume = LibraryLayoutResolver.ComputePath(LibraryLayout.Flat, BaseDir, null, FileName);

        Assert.Equal(Path.Join(BaseDir, FileName), withVolume.FullPath);
        Assert.Equal(ChapterPlacement.SeriesRoot, withVolume.Placement);
        Assert.Equal(Path.Join(BaseDir, FileName), withoutVolume.FullPath);
        Assert.Equal(ChapterPlacement.SeriesRoot, withoutVolume.Placement);
    }

    [Fact]
    public void VolumeFolder_WithVolume_PlacesInVolumeSubfolder()
    {
        var resolved = LibraryLayoutResolver.ComputePath(LibraryLayout.VolumeFolder, BaseDir, 2, FileName);

        Assert.Equal(Path.Join(BaseDir, "Vol 2", FileName), resolved.FullPath);
        Assert.Equal(ChapterPlacement.VolumeFolder, resolved.Placement);
    }

    [Fact]
    public void VolumeFolder_WithoutVolume_FallsBackToSeriesRoot_WithReason()
    {
        var resolved = LibraryLayoutResolver.ComputePath(LibraryLayout.VolumeFolder, BaseDir, null, FileName);

        Assert.Equal(Path.Join(BaseDir, FileName), resolved.FullPath);
        Assert.Equal(ChapterPlacement.SeriesRoot, resolved.Placement);
        Assert.Contains("volume", resolved.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VolumeCBZ_PlacesAtSeriesRoot_NotInVolumeFolder()
    {
        // VolumeCBZ chapters are merged into a single Vol N.cbz, so they stay flat at the series root
        // pre-bundle — no intermediate "Vol N/" folder, which Komga would treat as a stray series. See bug B.
        var withVolume = LibraryLayoutResolver.ComputePath(LibraryLayout.VolumeCBZ, BaseDir, 3, FileName);

        Assert.Equal(Path.Join(BaseDir, FileName), withVolume.FullPath);
        Assert.Equal(ChapterPlacement.SeriesRoot, withVolume.Placement);
    }

    [Fact]
    public void VolumeCBZ_WithoutVolume_FallsBackToSeriesRoot()
    {
        var resolved = LibraryLayoutResolver.ComputePath(LibraryLayout.VolumeCBZ, BaseDir, null, FileName);

        Assert.Equal(Path.Join(BaseDir, FileName), resolved.FullPath);
        Assert.Equal(ChapterPlacement.SeriesRoot, resolved.Placement);
    }

    [Fact]
    public void Resolve_DerivesPathFromSeriesLayoutAndChapterVolume()
    {
        var library = new SchemaFileLibrary("/library", "Lib");
        var manga = new SchemaManga("My Series", "", "http://example.com/img.jpg",
            SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.LibraryLayout = LibraryLayout.VolumeFolder;
        var chapter = new SchemaChapter(manga, "1", 4, null);

        var resolver = new LibraryLayoutResolver();
        string namingScheme = new KenkuSettings().ChapterNamingScheme;

        var resolved = resolver.Resolve(manga, chapter, namingScheme);

        string expectedFile = chapter.GetArchiveFileName(namingScheme);
        Assert.Equal(Path.Join(manga.FullDirectoryPath, "Vol 4", expectedFile), resolved.FullPath);
        Assert.Equal(ChapterPlacement.VolumeFolder, resolved.Placement);
    }
}
