using API.Schema.SeriesContext;
using API.Services;
using SchemaManga = API.Schema.SeriesContext.Series;
using SchemaFileLibrary = API.Schema.SeriesContext.FileLibrary;
using SchemaChapter = API.Schema.SeriesContext.Chapter;

namespace API.Tests.Services;

/// <summary>
/// Decides which volumes are ready to bundle under VolumeCBZ. "Ready" = every known chapter of the
/// volume is downloaded, none are already bundled, and the volume is closed (a later volume exists,
/// or the series will gain no more chapters). The trailing/in-progress volume is intentionally left
/// alone to avoid unbundle/rebundle churn when new chapters arrive.
/// </summary>
public class VolumeBundlePolicyTests
{
    private static SchemaManga MakeManga(LibraryLayout layout, SeriesReleaseStatus status = SeriesReleaseStatus.Continuing)
    {
        var library = new SchemaFileLibrary("/lib", "Lib");
        var manga = new SchemaManga("S", "", "http://x/c.jpg", status, [], [], [], [], library);
        manga.LibraryLayout = layout;
        manga.Chapters = new List<SchemaChapter>();
        return manga;
    }

    private static void AddChapter(SchemaManga manga, string number, int? volume, bool downloaded, bool bundled = false)
    {
        var chapter = new SchemaChapter(manga, number, volume, null) { Downloaded = downloaded };
        chapter.IsBundled = bundled;
        manga.Chapters.Add(chapter);
    }

    [Fact]
    public void NonVolumeCBZ_Layout_ReturnsNothing()
    {
        var manga = MakeManga(LibraryLayout.VolumeFolder);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "3", 2, downloaded: true);

        Assert.Empty(VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void ClosedVolume_FullyDownloaded_IsReady()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "2", 1, downloaded: true);
        AddChapter(manga, "3", 2, downloaded: false); // a later volume exists → vol 1 is closed

        Assert.Equal(new[] { 1 }, VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void TrailingVolume_NotReady_EvenWhenComplete()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "2", 1, downloaded: true); // only volume present, so it may still grow

        Assert.Empty(VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void IncompleteClosedVolume_NotReady()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "2", 1, downloaded: false); // missing a chapter
        AddChapter(manga, "3", 2, downloaded: true);

        Assert.Empty(VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void AlreadyBundledVolume_Excluded()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ);
        AddChapter(manga, "1", 1, downloaded: true, bundled: true);
        AddChapter(manga, "3", 2, downloaded: true);

        Assert.Empty(VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void CompletedSeries_TrailingVolume_IsReady()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ, SeriesReleaseStatus.Completed);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "2", 1, downloaded: true); // series is done → no more chapters coming

        Assert.Equal(new[] { 1 }, VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }

    [Fact]
    public void VolumelessChapters_AreIgnored()
    {
        var manga = MakeManga(LibraryLayout.VolumeCBZ);
        AddChapter(manga, "1", 1, downloaded: true);
        AddChapter(manga, "2", 2, downloaded: true);   // trailing
        AddChapter(manga, "100", null, downloaded: true); // loose, never bundled

        Assert.Equal(new[] { 1 }, VolumeBundlePolicy.VolumesReadyToBundle(manga));
    }
}
