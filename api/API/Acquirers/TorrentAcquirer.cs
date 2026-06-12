using API.DownloadClients.Interfaces;
using API.Indexers.Interfaces;
using API.Acquirers.Interfaces;
using API.Indexers;
using API.Connectors;
using API.Schema.SeriesContext;
using API.DownloadClients;
using log4net;

namespace API.Acquirers;

/// <summary>
/// Asynchronously acquires a chapter by handing the best matching torrent release off to an
/// external torrent client. A successful hand-off is <see cref="AcquireResult.Deferred"/> — the .cbz
/// is not on disk yet; <see cref="API.JobRuntime.Reconcilers.TorrentCompletionReconciler"/> finalises
/// the chapter once the torrent finishes. Idempotent: a torrent already known to the client is not
/// searched for or added again.
/// </summary>
public class TorrentAcquirer(
    IIndexerClient indexer,
    IDownloadClient downloadClient,
    ReleaseSelector selector,
    TorrentAcquirerSettings settings) : IChapterAcquirer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorrentAcquirer));

    public AcquisitionKind Kind => AcquisitionKind.Torrent;

    public async Task<AcquireResult> AcquireAsync(
        SourceId<Chapter> chapter,
        SeriesSource source,
        string saveArchiveFilePath,
        CancellationToken ct)
    {
        Chapter ch = chapter.Obj;
        Series series = ch.ParentManga;

        // A torrent for this chapter may already be in the client (a re-run after the hand-off job
        // succeeded, or a manual re-trigger) — never search/add a second one for the same chapter.
        switch (await downloadClient.GetStatus(chapter.Key, ct))
        {
            case DownloadStatus.Downloading or DownloadStatus.Completed:
                Log.DebugFormat("Torrent for {0} ch.{1} already in the client; deferring to completion.", series.Name, ch.ChapterNumber);
                return new AcquireResult.Deferred();
            case DownloadStatus.Errored errored:
                return new AcquireResult.Failed($"the torrent errored in the download client: {errored.Reason}");
        }

        var query = new IndexerQuery(
            SeriesTitle: series.Name,
            IssueNumber: ch.ChapterNumber,
            Year: series.Year?.ToString(),
            Categories: settings.IndexerCategories);

        IndexerSearchResult[] results = await indexer.Search(query, ct);
        IndexerSearchResult? best = selector.SelectBest(results);
        string tagKey = chapter.Key;

        if (best is null)
        {
            // No usable single-issue release — back catalogues often exist only as packs, so fall
            // back to a pack release whose issue range covers this chapter. Packs get a tag derived
            // from the release itself, so every chapter of the run converges on one client entry.
            IndexerSearchResult[] packs = await SearchCoveringPacks(series, ch, ct);
            best = selector.SelectBest(packs);
            if (best is null)
            {
                if (results.Length == 0 && packs.Length == 0)
                {
                    Log.InfoFormat("No torrent releases found for {0} ch.{1} (singles or packs)", series.Name, ch.ChapterNumber);
                    return new AcquireResult.Failed($"no torrent releases found for {series.Name} ch.{ch.ChapterNumber} (singles or packs)");
                }
                Log.InfoFormat("No torrent releases passed selection criteria for {0} ch.{1} ({2} candidates)",
                    series.Name, ch.ChapterNumber, results.Length + packs.Length);
                return new AcquireResult.Failed(
                    $"no release passed selection for {series.Name} ch.{ch.ChapterNumber} ({results.Length + packs.Length} candidates, min seeders {selector.MinSeeders})");
            }

            tagKey = PackTag.For(series.Key, best.DownloadUrl);
            switch (await downloadClient.GetStatus(tagKey, ct))
            {
                case DownloadStatus.Downloading or DownloadStatus.Completed:
                    Log.DebugFormat("Pack covering {0} ch.{1} already in the client; deferring to completion.", series.Name, ch.ChapterNumber);
                    return new AcquireResult.Deferred();
                case DownloadStatus.Errored errored:
                    return new AcquireResult.Failed($"the pack torrent errored in the download client: {errored.Reason}");
            }
        }

        string stagingDir = Path.Combine(settings.StagingDirectory, tagKey.Replace(':', '_'));
        Directory.CreateDirectory(stagingDir);

        string? tag = await downloadClient.Add(best.DownloadUrl, stagingDir, tagKey, ct);
        if (tag is null)
        {
            Log.WarnFormat("Torrent client refused release {0} for {1} ch.{2}", best.Title, series.Name, ch.ChapterNumber);
            return new AcquireResult.Failed($"the torrent client refused release '{best.Title}'");
        }

        Log.InfoFormat("Handed off torrent '{0}' for {1} ch.{2}; TorrentCompletionReconciler will finalise on completion.",
            best.Title, series.Name, ch.ChapterNumber);
        return new AcquireResult.Deferred();
    }

    /// <summary>Pack releases of this series whose issue range covers <paramref name="ch"/>.</summary>
    private async Task<IndexerSearchResult[]> SearchCoveringPacks(Series series, Chapter ch, CancellationToken ct)
    {
        // Decimal specials ("60.5") never appear in integer issue ranges.
        if (!int.TryParse(ch.ChapterNumber, out int issue))
            return [];

        IndexerSearchResult[] results = await indexer.Search(
            new IndexerQuery(series.Name, null, series.Year?.ToString(), settings.IndexerCategories), ct);
        return results.Where(r =>
        {
            ParsedRelease p = ReleaseTitleParser.Parse(r.Title);
            return string.Equals(p.SeriesTitle, series.Name, StringComparison.OrdinalIgnoreCase)
                   && p.IssueRange is { } range && issue >= range.Start && issue <= range.End;
        }).ToArray();
    }
}

/// <summary>Settings the TorrentAcquirer needs at construction time. Populated from KenkuSettings via DI.</summary>
public record TorrentAcquirerSettings(string StagingDirectory, int[] IndexerCategories);
