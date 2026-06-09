using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.DTOs;

/// <summary>
/// Per-series operational rollup: actual download progress, live job counts, and the most recent
/// failure. This is what the library badge and series page derive their state from — 'Downloading'
/// means work is actually pending, not merely 'a source is enabled'.
/// </summary>
public record SeriesRollup(string MangaId, int WantedChapters, int DownloadedChapters,
    int QueuedJobs, int RunningJobs, int NeedsAttentionJobs,
    string? LastError, DateTime? LastSyncAt, int? LastSyncChapterCount)
{
    [Required] public string MangaId { get; init; } = MangaId;
    [Required] [Description("Chapters wanted for download (a source link is enabled).")]
    public int WantedChapters { get; init; } = WantedChapters;
    [Required] [Description("Wanted chapters already on disk (downloaded or bundled).")]
    public int DownloadedChapters { get; init; } = DownloadedChapters;
    [Required] public int QueuedJobs { get; init; } = QueuedJobs;
    [Required] public int RunningJobs { get; init; } = RunningJobs;
    [Required] public int NeedsAttentionJobs { get; init; } = NeedsAttentionJobs;
    [Description("Most recent job failure message for this series, if any.")]
    public string? LastError { get; init; } = LastError;
    [Description("When chapters were last retrieved from a connector.")]
    public DateTime? LastSyncAt { get; init; } = LastSyncAt;
    [Description("How many chapters the connector reported on the last sync — 0 means 'looked and found none'.")]
    public int? LastSyncChapterCount { get; init; } = LastSyncChapterCount;
}
