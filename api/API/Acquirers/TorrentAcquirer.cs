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
        if (results.Length == 0)
        {
            Log.InfoFormat("No torrent releases found for {0} ch.{1}", series.Name, ch.ChapterNumber);
            return new AcquireResult.Failed($"no torrent releases found for {series.Name} ch.{ch.ChapterNumber}");
        }

        IndexerSearchResult? best = selector.SelectBest(results);
        if (best is null)
        {
            Log.InfoFormat("No torrent releases passed selection criteria for {0} ch.{1} ({2} candidates)",
                series.Name, ch.ChapterNumber, results.Length);
            return new AcquireResult.Failed(
                $"no release passed selection for {series.Name} ch.{ch.ChapterNumber} ({results.Length} candidates, min seeders {selector.MinSeeders})");
        }

        string stagingDir = Path.Combine(settings.StagingDirectory, chapter.Key);
        Directory.CreateDirectory(stagingDir);

        string? tag = await downloadClient.Add(best.DownloadUrl, stagingDir, chapter.Key, ct);
        if (tag is null)
        {
            Log.WarnFormat("Torrent client refused release {0} for {1} ch.{2}", best.Title, series.Name, ch.ChapterNumber);
            return new AcquireResult.Failed($"the torrent client refused release '{best.Title}'");
        }

        Log.InfoFormat("Handed off torrent '{0}' for {1} ch.{2}; TorrentCompletionReconciler will finalise on completion.",
            best.Title, series.Name, ch.ChapterNumber);
        return new AcquireResult.Deferred();
    }
}

/// <summary>Settings the TorrentAcquirer needs at construction time. Populated from KenkuSettings via DI.</summary>
public record TorrentAcquirerSettings(string StagingDirectory, int[] IndexerCategories);
