using API.Schema.SeriesContext;

namespace API.Services;

/// <summary>
/// Collapses the several uploads a connector may return for one chapter number (e.g. MangaDex scan
/// groups) into a single chapter, reconciling their titles: when the uploads disagree on a title we
/// trust none of them and clear it, so a mislabelled upload can't stamp a wrong title on the chapter.
/// A single agreed title (ignoring untitled uploads) is kept. Pure logic — unit-testable without HTTP/EF.
/// </summary>
public static class ChapterTitleReconciler
{
    public static (Chapter, SourceId<Chapter>)[] Reconcile(IEnumerable<(Chapter chapter, SourceId<Chapter> chapterId)> uploads)
    {
        return uploads
            .GroupBy(u => u.chapter.Key)
            .Select(group =>
            {
                (Chapter chapter, SourceId<Chapter> chapterId) representative = group.First();
                representative.chapter.Title = ResolveTitle(group.Select(u => u.chapter.Title));
                return representative;
            })
            .ToArray();
    }

    /// <summary>The title to show for a chapter given its uploads' titles: a single agreed title is kept;
    /// zero or conflicting titles resolve to null, so the chapter shows just its number rather than a
    /// guess. Shared by new-chapter creation and the self-healing backfill of existing chapters.</summary>
    public static string? ResolveTitle(IEnumerable<string?> uploadTitles)
    {
        List<string> distinctTitles = uploadTitles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct()
            .ToList()!;
        return distinctTitles.Count == 1 ? distinctTitles[0] : null;
    }
}
