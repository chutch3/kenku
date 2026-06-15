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
                List<string> distinctTitles = group
                    .Select(u => u.chapter.Title)
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Distinct()
                    .ToList()!;

                (Chapter chapter, SourceId<Chapter> chapterId) representative = group.First();
                // One agreed title is kept; zero or conflicting titles leave the chapter showing just its number.
                representative.chapter.Title = distinctTitles.Count == 1 ? distinctTitles[0] : null;
                return representative;
            })
            .ToArray();
    }
}
