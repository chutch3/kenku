using System.ComponentModel.DataAnnotations;
using API.Schema.ActionsContext.Actions.Generic;
using API.Schema.SeriesContext;

namespace API.Schema.ActionsContext.Actions;

public sealed class LibraryMovedActionRecord(Actions action, DateTime performedAt, string seriesId, string fileLibraryId)
    : ActionRecord(action, performedAt), IActionWithSeriesRecord
{
    public LibraryMovedActionRecord(Series manga, FileLibrary library) : this(Actions.LibraryMoved, DateTime.UtcNow, manga.Key, library.Key) { }
    
    /// <summary>
    /// <see cref="Schema.SeriesContext.FileLibrary"/> for which the cover was downloaded
    /// </summary>
    [StringLength(64)]
    public string FileLibraryId { get; init; } = fileLibraryId;

    public string SeriesId { get; init; } = seriesId;
}