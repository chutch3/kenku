using API.Connectors;
using API.Schema.SeriesContext;
using API.Services;
using Xunit;

namespace API.Tests.Unit.Services;

public class ChapterTitleReconcilerTests
{
    private static readonly Series Manga = new("Berserk", "", "url", SeriesReleaseStatus.Continuing, [], [], [], []);

    private static (Chapter, SourceId<Chapter>) Upload(string idOnSite, string? title)
    {
        var chapter = new Chapter(Manga, "1", 1, title);
        var sourceId = new SourceId<Chapter>(chapter, "MangaDex", idOnSite, null);
        return (chapter, sourceId);
    }

    [Fact]
    public void ConflictingTitles_AreCleared_SoNoWrongTitleIsShown()
    {
        var reconciled = ChapterTitleReconciler.Reconcile([
            Upload("a", "Tomb"),
            Upload("b", "The Shifting Water Mirror and the Residual Radiance of the Battle Flame"),
        ]);

        var chapter = Assert.Single(reconciled).Item1;
        Assert.Null(chapter.Title);
    }

    [Fact]
    public void AgreeingTitles_AreKept()
    {
        var reconciled = ChapterTitleReconciler.Reconcile([Upload("a", "Tomb"), Upload("b", "Tomb")]);

        Assert.Equal("Tomb", Assert.Single(reconciled).Item1.Title);
    }

    [Fact]
    public void AnUntitledUpload_DoesNotConflictWithATitledOne()
    {
        var reconciled = ChapterTitleReconciler.Reconcile([Upload("a", null), Upload("b", "Tomb")]);

        Assert.Equal("Tomb", Assert.Single(reconciled).Item1.Title);
    }

    [Fact]
    public void ASingleUpload_KeepsItsTitle()
    {
        var reconciled = ChapterTitleReconciler.Reconcile([Upload("a", "Tomb")]);

        Assert.Equal("Tomb", Assert.Single(reconciled).Item1.Title);
    }

    [Fact]
    public void AllUntitled_StaysNull()
    {
        var reconciled = ChapterTitleReconciler.Reconcile([Upload("a", null), Upload("b", null)]);

        Assert.Null(Assert.Single(reconciled).Item1.Title);
    }

    [Fact]
    public void DistinctChapters_AreEachReturnedOnce()
    {
        var ch2 = new Chapter(Manga, "2", 1, "Anadi-avidya");
        var reconciled = ChapterTitleReconciler.Reconcile([
            Upload("a", "Tomb"),
            (ch2, new SourceId<Chapter>(ch2, "MangaDex", "c", null)),
        ]);

        Assert.Equal(2, reconciled.Length);
    }
}
