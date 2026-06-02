using API.Services;

namespace API.Controllers.DTOs;

/// <summary>One volume with its chapter list and bundling status.</summary>
public record VolumeEntry(
    int VolumeNumber,
    string? Title,
    bool IsBundled,
    string? ArchiveFileName,
    int ChapterCount,
    List<ChapterFileEntry> Chapters,
    int DownloadedChapterCount,
    VolumeBundleState BundleState,
    string BundleReason
);
