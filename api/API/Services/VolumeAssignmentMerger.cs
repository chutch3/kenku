using API.Services.Interfaces;
using API.Schema.SeriesContext;

namespace API.Services;

/// <summary>
/// Merges partial volume maps from several sources into the set of changes to apply, with these rules:
/// <list type="bullet">
///   <item>A <see cref="MetadataConfidence.Manual"/> assignment is a hard floor — never overridden.</item>
///   <item>Higher-confidence sources win; within the same confidence, earlier (higher-priority) sources win.</item>
///   <item>Resolution is re-runnable: a stale lower/equal-confidence guess is overwritten when a better
///         result appears, but an existing assignment is never <em>downgraded</em> to a weaker source.</item>
///   <item>A chapter no source resolves is left exactly as-is — automatic resolution never clears a volume.</item>
/// </list>
/// Pure logic so the precedence/lock/overwrite behavior is unit-testable without HTTP, files, or EF.
/// </summary>
public static class VolumeAssignmentMerger
{
    private static int Rank(MetadataConfidence confidence) => confidence switch
    {
        MetadataConfidence.Manual => 3,
        MetadataConfidence.Exact => 2,
        MetadataConfidence.Heuristic => 1,
        _ => 0,
    };

    /// <param name="resultsInPriorityOrder">Source results, highest priority first.</param>
    public static IReadOnlyList<VolumeChange> ComputeChanges(
        IEnumerable<Chapter> chapters,
        IReadOnlyList<VolumeResolverResult> resultsInPriorityOrder)
    {
        var changes = new List<VolumeChange>();

        foreach (var chapter in chapters)
        {
            // The manual floor: a user assignment is never touched by automatic resolution.
            if (chapter.MetadataConfidence == MetadataConfidence.Manual)
                continue;

            // Pick the best candidate across sources: highest confidence, ties broken by priority order.
            int? bestVolume = null;
            MetadataConfidence bestConfidence = default;
            foreach (var result in resultsInPriorityOrder)
            {
                if (!result.Map.TryGetValue(chapter.ChapterNumber, out int volume))
                    continue;
                if (bestVolume is null || Rank(result.Confidence) > Rank(bestConfidence))
                {
                    bestVolume = volume;
                    bestConfidence = result.Confidence;
                }
            }

            // No source resolved this chapter — leave whatever is already there (never null it out).
            if (bestVolume is null)
                continue;

            int currentRank = chapter.VolumeNumber is null
                ? 0
                : Rank(chapter.MetadataConfidence ?? MetadataConfidence.Heuristic);

            // Don't downgrade an existing, stronger assignment to a weaker source.
            if (chapter.VolumeNumber is not null && Rank(bestConfidence) < currentRank)
                continue;

            // No-op if it already matches.
            if (chapter.VolumeNumber == bestVolume && chapter.MetadataConfidence == bestConfidence)
                continue;

            changes.Add(new VolumeChange(chapter, bestVolume.Value, bestConfidence));
        }

        return changes;
    }
}
