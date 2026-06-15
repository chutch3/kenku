using API.Schema.SeriesContext;

namespace API.Services;

/// <summary>
/// Self-heals chapters that pre-date scan-group capture. For each existing chapter the connector still
/// reports, it backfills missing <see cref="SourceId{T}.ScanGroup"/>/<see cref="SourceId{T}.Language"/>
/// on the stored source rows from the current uploads, and re-resolves the chapter title (clearing a
/// title the uploads now disagree on). Already-set group/language is never overwritten. Pure logic —
/// mutates the passed entities so the caller's <c>Sync</c> persists them.
/// </summary>
public static class ChapterSyncBackfill
{
    public static void Apply(
        IEnumerable<Chapter> existingChapters,
        IEnumerable<(Chapter chapter, SourceId<Chapter> chapterId)> fetchedUploads)
    {
        Dictionary<string, Chapter> existingByKey = existingChapters.ToDictionary(c => c.Key);

        foreach (var group in fetchedUploads.GroupBy(u => u.chapter.Key))
        {
            if (!existingByKey.TryGetValue(group.Key, out Chapter? existing))
                continue;

            existing.Title = ChapterTitleReconciler.ResolveTitle(group.Select(u => u.chapter.Title));

            foreach ((Chapter _, SourceId<Chapter> fetchedId) in group)
            {
                SourceId<Chapter>? match = existing.SourceIds.FirstOrDefault(s =>
                    s.MangaConnectorName == fetchedId.MangaConnectorName &&
                    s.IdOnConnectorSite == fetchedId.IdOnConnectorSite);
                if (match is null)
                    continue;
                if (string.IsNullOrEmpty(match.ScanGroup))
                    match.ScanGroup = fetchedId.ScanGroup;
                if (string.IsNullOrEmpty(match.Language))
                    match.Language = fetchedId.Language;
            }
        }
    }
}
