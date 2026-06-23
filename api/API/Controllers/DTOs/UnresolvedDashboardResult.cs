using System.ComponentModel.DataAnnotations;

namespace API.Controllers.DTOs;

/// <summary>
/// Entry for a manga with unresolved chapters or missing files.
/// </summary>
public record UnresolvedSeriesEntry(
    [Required] string SeriesId,
    [Required] string SeriesName,
    [Required] int UnresolvedChapterCount,
    [Required] int MissingFileCount
);

/// <summary>
/// Result of GET /v2/Library/unresolved
/// </summary>
public record UnresolvedDashboardResult(
    [Required] List<UnresolvedSeriesEntry> Series
);
