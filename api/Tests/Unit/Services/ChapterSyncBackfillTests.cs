using API.Connectors;
using API.Schema.SeriesContext;
using API.Services;
using Xunit;

namespace API.Tests.Unit.Services;

public class ChapterSyncBackfillTests
{
    private static readonly Series Manga = new("Berserk", "", "url", SeriesReleaseStatus.Continuing, [], [], [], []);

    private static Chapter ExistingChapter(string number, string? title, params (string idOnSite, string? group, string? lang)[] sources)
    {
        var chapter = new Chapter(Manga, number, 1, title);
        foreach (var (idOnSite, group, lang) in sources)
            chapter.SourceIds.Add(new SourceId<Chapter>(chapter, "MangaDex", idOnSite, null, false, group, lang));
        return chapter;
    }

    private static (Chapter, SourceId<Chapter>) Upload(string number, string idOnSite, string? title, string? group, string? lang)
    {
        var chapter = new Chapter(Manga, number, 1, title);
        return (chapter, new SourceId<Chapter>(chapter, "MangaDex", idOnSite, null, false, group, lang));
    }

    [Fact]
    public void BackfillsMissingScanGroupAndLanguage_FromTheMatchingUpload()
    {
        var existing = ExistingChapter("384", "x", ("uuid-a", null, null));

        ChapterSyncBackfill.Apply([existing], [Upload("384", "uuid-a", "Tomb", "Evil Genius", "en")]);

        var source = Assert.Single(existing.SourceIds);
        Assert.Equal("Evil Genius", source.ScanGroup);
        Assert.Equal("en", source.Language);
    }

    [Fact]
    public void DoesNotOverwriteAnAlreadySetScanGroup()
    {
        var existing = ExistingChapter("384", "x", ("uuid-a", "Original Group", "en"));

        ChapterSyncBackfill.Apply([existing], [Upload("384", "uuid-a", "Tomb", "Different Group", "fr")]);

        var source = Assert.Single(existing.SourceIds);
        Assert.Equal("Original Group", source.ScanGroup);
        Assert.Equal("en", source.Language);
    }

    [Fact]
    public void ClearsTheTitle_WhenUploadsDisagree()
    {
        var existing = ExistingChapter("384", "The Shifting Water Mirror", ("uuid-a", null, null), ("uuid-b", null, null));

        ChapterSyncBackfill.Apply([existing], [
            Upload("384", "uuid-a", "Tomb", "Evil Genius", "en"),
            Upload("384", "uuid-b", "The Shifting Water Mirror", "Aqua Scans", "en"),
        ]);

        Assert.Null(existing.Title);
        Assert.Equal("Evil Genius", existing.SourceIds.Single(s => s.IdOnConnectorSite == "uuid-a").ScanGroup);
        Assert.Equal("Aqua Scans", existing.SourceIds.Single(s => s.IdOnConnectorSite == "uuid-b").ScanGroup);
    }

    [Fact]
    public void KeepsAnAgreedTitle()
    {
        var existing = ExistingChapter("383", null, ("uuid-a", null, null));

        ChapterSyncBackfill.Apply([existing], [Upload("383", "uuid-a", "Anadi-avidya", "Evil Genius", "en")]);

        Assert.Equal("Anadi-avidya", existing.Title);
    }

    [Fact]
    public void LeavesChaptersWithNoMatchingUploadUntouched()
    {
        var existing = ExistingChapter("999", "Real Title", ("uuid-z", "Keep", "en"));

        ChapterSyncBackfill.Apply([existing], [Upload("384", "uuid-a", "Tomb", "Evil Genius", "en")]);

        Assert.Equal("Real Title", existing.Title);
        Assert.Equal("Keep", Assert.Single(existing.SourceIds).ScanGroup);
    }
}
