namespace API.Acquirers;

/// <summary>
/// Flags pages that look like "missing page" placeholders within a chapter. The pages of one chapter
/// are all roughly the same resolution; a placeholder graphic (or a page that failed to download/decode,
/// area 0) is dramatically smaller. Each page's <b>pixel area</b> is compared to the chapter's
/// <b>largest</b> page — relative-to-max, not to the median, so detection still works when the bad pages
/// are the majority (the case that prompted this: Crossed served placeholders for 13 of 21 pages, so any
/// median/percentile rule would treat the placeholders as "normal" and miss them).
///
/// Pixel area, not byte size, on purpose: a near-blank page is tiny in bytes but full resolution, so a
/// byte-size rule false-positives on legitimate simple pages where resolution does not.
/// </summary>
public static class MissingPageDetector
{
    /// <summary>A page smaller than this fraction of the chapter's largest page is treated as a
    /// placeholder. 0.15 clears real variation by a wide margin — a double-page spread only leaves single
    /// pages at ~50% of the max — while an actual placeholder sits well under 1%.</summary>
    private const double MinFractionOfLargest = 0.15;

    /// <summary>If even the largest page is below this, there is no real page to compare against — the
    /// whole chapter is placeholders. 256×256 px; every real comic page is far larger.</summary>
    private const long MinPlausiblePageArea = 256 * 256;

    /// <summary>
    /// Returns the indices of pages that look like placeholders / missing data. Pass area <c>0</c> for a
    /// page that couldn't be downloaded or decoded — it is always flagged.
    /// </summary>
    public static IReadOnlyList<int> Detect(IReadOnlyList<long> pageAreas)
    {
        if (pageAreas.Count == 0)
            return [];

        long largest = pageAreas.Max();
        // The biggest page is itself placeholder-sized → nothing real to anchor on → the whole chapter.
        if (largest < MinPlausiblePageArea)
            return Enumerable.Range(0, pageAreas.Count).ToList();

        double threshold = largest * MinFractionOfLargest;
        return Enumerable.Range(0, pageAreas.Count).Where(i => pageAreas[i] < threshold).ToList();
    }
}
