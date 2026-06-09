using API.Schema.ActionsContext.Actions.Generic;
using API.Schema.SeriesContext;

namespace API.Schema.ActionsContext.Actions;

public sealed class ChaptersRetrievedActionRecord(Actions action, DateTime performedAt, string mangaId)
    : ActionRecord(action, performedAt), IActionWithMangaRecord
{
    public ChaptersRetrievedActionRecord(Series manga, int chapterCount)
        : this(Actions.ChaptersRetrieved, DateTime.UtcNow, manga.Key)
    {
        ChapterCount = chapterCount;
    }

    public string MangaId { get; init; } = mangaId;

    /// <summary>How many chapters the connector reported — 0 here is "looked and found none",
    /// distinguishable from never having synced at all.</summary>
    public int ChapterCount { get; init; }
}