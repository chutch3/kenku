using System.IO.Compression;
using System.Text.RegularExpressions;
using API.Schema.SeriesContext;
using API.Workers.MaintenanceWorkers;
using log4net;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace API.Services;

/// <summary>
/// Resolves a single series' chapter→volume assignments: establishes/refreshes its MangaDex link
/// (id-match over fuzzy score), applies exact sources merged by confidence, then fills the rest with the
/// colour-cover heuristic — never clobbering a manual assignment. This is the domain logic behind the
/// ResolveSeriesVolumes job and the legacy resolve worker, so both behave identically during migration.
/// </summary>
public class VolumeResolutionService(
    KenkuSettings settings,
    IMangaDexVolumeResolver mangaDexVolumeResolver,
    IMangaDexSearchService mangaDexSearchService,
    IEnumerable<IVolumeResolver>? volumeResolvers = null)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VolumeResolutionService));

    public async Task ResolveAsync(SeriesContext context, string mangaId, CancellationToken ct)
    {
        var manga = await context.Series
            .Include(m => m.SourceIds)
            .Include(m => m.Library)
            .Include(m => m.MetadataSource)
            .FirstOrDefaultAsync(m => m.Key == mangaId, ct);

        if (manga is null)
        {
            Log.Warn($"Series {mangaId} not found in database; skipping.");
            return;
        }

        // Load ALL chapters, downloaded or not. Exact sources (MangaDex/Wikipedia) map chapter
        // number -> volume and need no files, so they can place a chapter long before it downloads —
        // the full volume layout should be visible up front. Only the color heuristic below is gated
        // on the downloaded .cbz. We also load already-assigned chapters so an exact source can correct
        // a stale heuristic guess on a later run; manual assignments are protected inside the merger.
        var chapters = await context.Chapters
            .Where(c => c.ParentMangaId == mangaId)
            .ToListAsync(ct);

        if (chapters.Count == 0)
            return;

        chapters = chapters.OrderBy(c => c, new Chapter.ChapterComparer()).ToList();
        int unresolved = chapters.Count(c => c.VolumeNumber == null);
        Log.Info($"Resolving volumes for {manga.Name} ({chapters.Count} chapters, {unresolved} unresolved)...");

        // Establish/refresh a MangaDex link. Always attempt for an Unlinked series; also re-attempt for a
        // NoMatch/Ambiguous series that now carries an AniList link we can match on. Never re-touch a
        // series that is already confidently linked (AutoMatched/Confirmed).
        bool confidentlyLinked = manga.MetadataSource?.Status
            is MetadataSourceStatus.AutoMatched or MetadataSourceStatus.Confirmed;
        if (!confidentlyLinked &&
            (manga.MetadataSource?.Status == MetadataSourceStatus.Unlinked || GetAniListId(manga) is not null))
            await TryAutoMatch(context, manga, chapters, ct);

        bool exactAllowed = settings.VolumeResolutionStrategy
            is VolumeResolutionStrategy.ExactOnly or VolumeResolutionStrategy.ExactThenGuess;

        // Step 1: exact sources — merged by confidence, re-runnable, never overriding a manual fix.
        if (exactAllowed)
            await ApplyExactSources(manga, chapters, ct);

        // Step 2: color heuristic fills whatever the exact sources couldn't; anything still
        // unresolved is intentionally left loose for the user to assign.
        if (settings.VolumeResolutionStrategy == VolumeResolutionStrategy.ExactThenGuess)
        {
            // The heuristic inspects the downloaded .cbz, so it can only consider chapters with a file.
            var stillUnresolved = chapters
                .Where(c => c.VolumeNumber == null && c.Downloaded && c.FileName != null)
                .ToList();
            if (stillUnresolved.Count > 0)
            {
                int startVolume = chapters.Where(c => c.VolumeNumber != null)
                    .Select(c => c.VolumeNumber!.Value).DefaultIfEmpty(0).Max();
                await TryResolveWithColorHeuristic(stillUnresolved, startVolume, ct);
            }
        }

        if (await context.Sync(ct, typeof(VolumeResolutionService), nameof(ResolveAsync)) is { success: false } err)
            Log.Error($"Failed to save volume updates for {manga.Name}: {err.exceptionMessage}");
    }

    /// <summary>
    /// Runs the exact metadata sources (currently MangaDex), merges them by confidence, and applies
    /// the resulting changes. Manual assignments are never touched and a stronger existing assignment
    /// is never downgraded — see <see cref="VolumeAssignmentMerger"/>.
    /// </summary>
    private async Task ApplyExactSources(Series manga, List<Chapter> chapters, CancellationToken ct)
    {
        var results = new List<VolumeResolverResult>();

        try
        {
            var mangaDexMap = await mangaDexVolumeResolver.GetChapterToVolumeMapAsync(manga, ct);
            if (mangaDexMap.Count > 0)
                results.Add(new VolumeResolverResult(MetadataConfidence.Exact, mangaDexMap));
        }
        catch (Exception ex)
        {
            Log.Error($"MangaDex volume resolution failed for {manga.Name}: {ex.Message}");
        }

        // Additional sources (e.g. Wikipedia) fill chapters MangaDex doesn't cover. MangaDex is listed
        // first, so it wins ties within the same confidence; the others contribute the holes.
        foreach (var resolver in volumeResolvers ?? [])
        {
            try
            {
                var map = await resolver.ResolveAsync(manga, chapters, ct);
                if (map.Count > 0)
                    results.Add(new VolumeResolverResult(resolver.Confidence, map));
            }
            catch (Exception ex)
            {
                Log.Error($"{resolver.SourceName} volume resolution failed for {manga.Name}: {ex.Message}");
            }
        }

        if (results.Count == 0)
            return;

        var changes = VolumeAssignmentMerger.ComputeChanges(chapters, results);
        foreach (var change in changes)
            AssignVolume(change.Chapter, change.Volume, change.Confidence);

        if (changes.Count > 0)
            Log.Info($"Applied {changes.Count} exact volume assignment(s) for {manga.Name}.");
    }

    /// <summary>
    /// Attempts to auto-match the manga against MangaDex using weighted scoring.
    /// Sets MetadataSource.Status to AutoMatched, Ambiguous, or NoMatch and saves.
    /// If AutoMatched, immediately resolves volumes via the new ExternalId.
    /// If the volume fetch returns 0 mappings, rolls back to Unlinked.
    /// </summary>
    private async Task TryAutoMatch(SeriesContext context, Series manga, List<Chapter> allChapters, CancellationToken ct)
    {
        string normalizedTitle = NormalizeTitle(manga.Name);

        List<MangaDexSearchResult> candidates;
        try
        {
            candidates = await mangaDexSearchService.SearchAsync(normalizedTitle, ct);
        }
        catch (Exception ex)
        {
            Log.Error($"Auto-match search failed for {manga.Name}: {ex.Message}");
            return;
        }

        if (candidates.Count == 0)
        {
            Log.Info($"Auto-match: no candidates found for {manga.Name}. Setting NoMatch.");
            manga.MetadataSource!.Status = MetadataSourceStatus.NoMatch;
            await context.Sync(ct, typeof(VolumeResolutionService), nameof(TryAutoMatch));
            return;
        }

        string topId;
        string decision;
        float topScore;
        float secondScore = 0f;
        MetadataSourceStatus newStatus;

        // An exact AniList-id match is authoritative — take it over any fuzzy title/count score. This is
        // what disambiguates e.g. a manga from its same-named webcomic when the title alone can't.
        string? ourAniListId = GetAniListId(manga);
        MangaDexSearchResult? idMatch = ourAniListId is null
            ? null
            : candidates.FirstOrDefault(c => string.Equals(c.AniListId, ourAniListId, StringComparison.Ordinal));

        if (idMatch is not null)
        {
            topId = idMatch.MangaDexId;
            topScore = 1f;
            newStatus = MetadataSourceStatus.AutoMatched;
            decision = "AutoMatched(anilist)";
        }
        else
        {
            int ourChapterCount = allChapters.Count;
            var scored = candidates
                .Select(c => (candidate: c, score: ScoreCandidate(normalizedTitle, ourChapterCount, c)))
                .OrderByDescending(x => x.score)
                .ToList();

            topScore = scored[0].score;
            secondScore = scored.Count > 1 ? scored[1].score : 0f;
            topId = scored[0].candidate.MangaDexId;

            if (topScore >= 0.90f && (scored.Count == 1 || topScore - secondScore >= 0.10f))
            {
                newStatus = MetadataSourceStatus.AutoMatched;
                decision = "AutoMatched";
            }
            else if (topScore >= 0.50f && scored.Count > 1 && topScore - secondScore < 0.10f)
            {
                newStatus = MetadataSourceStatus.Ambiguous;
                decision = "Ambiguous";
            }
            else
            {
                // Single candidate 0.50–0.90, or top below 0.50: not confident enough to link.
                newStatus = MetadataSourceStatus.NoMatch;
                decision = "NoMatch";
            }
        }

        Log.Info($"Auto-match decision: mangaId={manga.Key} topCandidateId={topId} topScore={topScore:F4} secondScore={secondScore:F4} decision={decision}");

        if (newStatus == MetadataSourceStatus.AutoMatched)
        {
            // An id match is authoritative — persist the link even if no volumes come back yet (they can
            // arrive from the all-languages aggregate, Wikipedia, or a later run). A scored match is only
            // trusted enough to commit when it actually yields volumes; otherwise it's rolled back.
            Dictionary<string, int> map;
            try
            {
                map = await mangaDexSearchService.GetChapterToVolumeMapAsync(topId, ct);
            }
            catch (Exception ex)
            {
                Log.Error($"Auto-match volume fetch failed for {manga.Name} (externalId={topId}): {ex.Message}");
                if (idMatch is null)
                    return; // scored match: roll back on fetch failure
                map = [];
            }

            if (map.Count == 0 && idMatch is null)
            {
                Log.Info($"Auto-match succeeded for {manga.Name} but volume fetch returned 0 mappings. Rolling back to Unlinked.");
                await context.Sync(ct, typeof(VolumeResolutionService), nameof(TryAutoMatch));
                return;
            }

            // Commit the auto-match
            manga.MetadataSource!.ExternalId = topId;
            manga.MetadataSource.Status = MetadataSourceStatus.AutoMatched;
            manga.MetadataSource.MatchScore = topScore;

            // Apply volume assignments with Exact confidence, but never over a manual assignment —
            // the manual floor applies here too, not just in the merger.
            int mapped = 0;
            foreach (var chapter in allChapters)
            {
                if (chapter.MetadataConfidence == MetadataConfidence.Manual)
                    continue;
                if (map.TryGetValue(chapter.ChapterNumber, out int vol))
                {
                    AssignVolume(chapter, vol, MetadataConfidence.Exact);
                    mapped++;
                }
            }

            Log.Info($"Auto-match applied {mapped}/{allChapters.Count} volume assignments for {manga.Name}.");
        }
        else
        {
            manga.MetadataSource!.Status = newStatus;
        }

        await context.Sync(ct, typeof(VolumeResolutionService), nameof(TryAutoMatch));
    }

    // No real tankōbon volume holds anywhere near this many chapters. If the heuristic groups
    // more than this without finding a new color cover, it has lost the volume boundary (e.g. a
    // long run of black-and-white covers) and must not fabricate a giant trailing volume.
    private const int MaxPlausibleVolumeChapters = 40;

    private async Task TryResolveWithColorHeuristic(List<Chapter> chapters, int startVolume, CancellationToken ct)
    {
        int currentVolume = startVolume;
        bool isFirstChapter = true;
        bool prevWasColor = false;

        // Chapters are buffered into the current volume and only committed once that volume is
        // closed by the next color cover (or, for the trailing volume, at the end of the list).
        // This lets us discard an implausibly large group instead of dumping every unresolved
        // chapter into one bogus volume.
        var pendingVolumeChapters = new List<Chapter>();

        void CommitPending()
        {
            foreach (var c in pendingVolumeChapters)
                AssignVolume(c, currentVolume, MetadataConfidence.Heuristic);
            pendingVolumeChapters.Clear();
        }

        foreach (var chapter in chapters)
        {
            string? filePath = chapter.FullArchiveFilePath;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Log.Warn($"File not found for chapter {chapter.ChapterNumber}, skipping.");
                continue;
            }

            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var images = archive.Entries
                    .Where(e => e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                e.FullName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => Path.GetFileNameWithoutExtension(e.FullName).Equals("cover", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(e => e.FullName)
                    .ToList();

                if (images.Count == 0) continue;

                using var stream = images[0].Open();
                using var image = await Image.LoadAsync<Rgb24>(stream, ct);
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(200, 200), Mode = ResizeMode.Max }));

                long colorDiffSum = 0;
                int pixelCount = image.Width * image.Height;
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgb24> row = accessor.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                        {
                            ref Rgb24 pixel = ref row[x];
                            colorDiffSum += Math.Abs(pixel.R - pixel.G) +
                                            Math.Abs(pixel.G - pixel.B) +
                                            Math.Abs(pixel.B - pixel.R);
                        }
                    }
                });

                double avgDiff = (double)colorDiffSum / pixelCount;
                bool isColor = avgDiff > 10;

                if (isFirstChapter)
                {
                    isFirstChapter = false;
                    if (!isColor && currentVolume == 0)
                    {
                        Log.Info($"First chapter ({chapter.ChapterNumber}) has no color cover. Aborting heuristic.");
                        break;
                    }
                }
                else if (isColor && prevWasColor)
                {
                    Log.Info($"Consecutive color covers at chapter {chapter.ChapterNumber}. Aborting heuristic.");
                    break;
                }

                prevWasColor = isColor;

                if (isColor)
                {
                    // A new color cover closes the previous volume — commit it, then open the next.
                    CommitPending();
                    currentVolume++;
                    Log.Debug($"Color cover on chapter {chapter.ChapterNumber} (avgDiff={avgDiff:F2}). Starting volume {currentVolume}.");
                }

                if (currentVolume > 0)
                {
                    pendingVolumeChapters.Add(chapter);
                    if (pendingVolumeChapters.Count > MaxPlausibleVolumeChapters)
                    {
                        Log.Info($"Heuristic volume {currentVolume} exceeded {MaxPlausibleVolumeChapters} chapters without a new color cover; aborting to avoid a bogus volume.");
                        pendingVolumeChapters.Clear();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in color heuristic for chapter {chapter.ChapterNumber}: {ex.Message}");
                if (currentVolume > 0)
                    pendingVolumeChapters.Add(chapter);
            }
        }

        // Commit the trailing volume. The size guard above guarantees it is plausibly sized.
        CommitPending();
    }

    private static void AssignVolume(Chapter chapter, int volume, MetadataConfidence confidence)
    {
        chapter.VolumeNumber = volume;
        chapter.MetadataConfidence = confidence;
    }

    private static float JaroWinkler(string s1, string s2)
    {
        if (s1 == s2) return 1.0f;
        if (s1.Length == 0 || s2.Length == 0) return 0.0f;

        int matchWindow = Math.Max(s1.Length, s2.Length) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;

        bool[] s1Matches = new bool[s1.Length];
        bool[] s2Matches = new bool[s2.Length];

        int matches = 0;
        int transpositions = 0;

        for (int i = 0; i < s1.Length; i++)
        {
            int start = Math.Max(0, i - matchWindow);
            int end = Math.Min(i + matchWindow + 1, s2.Length);

            for (int j = start; j < end; j++)
            {
                if (s2Matches[j] || s1[i] != s2[j]) continue;
                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0f;

        int k = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        float jaro = ((float)matches / s1.Length +
                      (float)matches / s2.Length +
                      (float)(matches - transpositions / 2) / matches) / 3.0f;

        // Winkler prefix bonus (up to 4 chars)
        int prefix = 0;
        for (int i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)); i++)
        {
            if (s1[i] == s2[i]) prefix++;
            else break;
        }

        return jaro + prefix * 0.1f * (1.0f - jaro);
    }

    private static float ScoreCandidate(string normalizedMangaTitle, int ourChapterCount, MangaDexSearchResult candidate)
    {
        string candidateTitle = NormalizeTitle(candidate.Title);
        float titleSim = JaroWinkler(normalizedMangaTitle, candidateTitle);

        // MangaDex's lastChapter is frequently empty for ongoing series (e.g. Dandadan), which
        // leaves us with no chapter-count signal. When the count is unavailable, score on title
        // alone — otherwise a zero count-proximity drags a perfect title match (1.0) down to 0.65
        // and a correct, sole candidate gets rejected as NoMatch.
        if (ourChapterCount <= 0 || candidate.ChapterCount <= 0)
            return titleSim;

        int maxCount = Math.Max(ourChapterCount, candidate.ChapterCount);
        float countProximity = 1.0f - (float)Math.Abs(ourChapterCount - candidate.ChapterCount) / maxCount;
        return titleSim * 0.65f + countProximity * 0.35f;
    }

    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static string NormalizeTitle(string title)
    {
        string lower = title.ToLowerInvariant();
        string noPunct = PunctuationRegex.Replace(lower, " ");
        return WhitespaceRegex.Replace(noPunct, " ").Trim();
    }

    private static readonly Regex AniListIdRegex = new(@"anilist\.co/manga/(\d+)", RegexOptions.Compiled);

    /// <summary>The AniList id from the series' external links, if it carries one (e.g. captured from a
    /// connector page). This is the identifier we match against a MangaDex entry's <c>links.al</c>.</summary>
    private static string? GetAniListId(Series manga)
    {
        foreach (Link link in manga.Links)
        {
            if (!link.LinkProvider.Equals("AniList", StringComparison.OrdinalIgnoreCase))
                continue;
            Match m = AniListIdRegex.Match(link.LinkUrl);
            if (m.Success)
                return m.Groups[1].Value;
        }
        return null;
    }
}
