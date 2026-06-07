using API.Schema.SeriesContext;

namespace API.Services.Interfaces;

/// <summary>
/// A single source of volume information (MangaDex, a scraped chapter list, the color heuristic, …).
/// Each resolver returns a <em>partial</em> chapter-number → volume map — only what it actually knows.
/// The orchestrator merges the results from all resolvers by confidence and fills the holes.
/// </summary>
public interface IVolumeResolver
{
    /// <summary>Human-readable provenance, e.g. "MangaDex" or "Wikipedia". Used for logging.</summary>
    string SourceName { get; }

    /// <summary>The confidence that assignments from this source carry.</summary>
    MetadataConfidence Confidence { get; }

    /// <summary>
    /// Resolve volume numbers for the given chapters. Keys are normalized chapter numbers
    /// (matching <see cref="Chapter.ChapterNumber"/>); only resolved chapters need be present.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> ResolveAsync(
        Series manga, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken);
}

/// <summary>One source's result, paired with the confidence it carries, in priority order.</summary>
public readonly record struct VolumeResolverResult(
    MetadataConfidence Confidence, IReadOnlyDictionary<string, int> Map);

/// <summary>A single volume assignment the merger decided should be written.</summary>
public readonly record struct VolumeChange(Chapter Chapter, int Volume, MetadataConfidence Confidence);
