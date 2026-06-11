using API.Acquirers;
using API.DownloadClients.Interfaces;
using API.JobRuntime.Interfaces;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

public enum ChangeLibraryStatus
{
    Ok,
    LibraryNotFound,
    SeriesNotFound,
    ConnectorNotFound,
    ConnectorSeriesNotFound,
    SaveFailed
}

/// <summary>
/// The add/move-to-library workflow: import the series from its connector when it isn't known yet,
/// track it, optionally enable the originating source (the one-decision add flow), queue moves for
/// already-downloaded files when the library changes, and queue cover + chapter sync so a freshly
/// added series is never an empty shell. Deletion is the mirror: the series goes, and so does its
/// operational residue (outstanding jobs, tagged torrents).
/// </summary>
public class SeriesLibraryService(KenkuSettings settings, IEnumerable<SeriesSource> connectors, IJobStore jobStore, IClock clock,
    JobRuntime.RunningJobRegistry running, IDownloadClient? downloadClient = null)
{
    private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(SeriesLibraryService));

    /// <summary>
    /// Deletes the series and sweeps what would otherwise outlive it: jobs keyed on the series are
    /// cancelled/removed (a leftover job retries against vanished SourceIds into a ghost
    /// NeedsAttention row), and torrents tagged with its chapter source-ids are released from the
    /// download client (best-effort — the client may be down or the torrent already gone).
    /// </summary>
    public async Task<bool> DeleteAsync(SeriesContext context, Schema.JobsContext.JobsContext jobsContext,
        string mangaId, CancellationToken ct)
    {
        if (await context.Series.FirstOrDefaultAsync(m => m.Key == mangaId, ct) is not { } manga)
            return false;

        // Only torrent-kind sources tag the download client, and each Remove is an HTTP round-trip —
        // sweeping every scrape/archive chapter made deleting a long series take minutes.
        List<string> torrentConnectorNames = connectors
            .Where(c => c.Kind == AcquisitionKind.Torrent).Select(c => c.Name).ToList();
        List<string> torrentTags = await context.MangaConnectorToChapter
            .Where(id => id.Obj.ParentMangaId == mangaId && torrentConnectorNames.Contains(id.MangaConnectorName))
            .Select(id => id.Key)
            .ToListAsync(ct);

        List<string> runningJobKeys = await jobsContext.JobQueue
            .Where(j => j.ResourceKey == mangaId && j.Status == JobStatus.Running)
            .Select(j => j.Key)
            .ToListAsync(ct);
        foreach (string jobKey in runningJobKeys)
            running.Cancel(jobKey);
        await jobsContext.JobQueue
            .Where(j => j.ResourceKey == mangaId && j.Status != JobStatus.Running)
            .ExecuteDeleteAsync(ct);

        if (downloadClient is not null)
            foreach (string tag in torrentTags)
            {
                try { await downloadClient.Remove(tag, deleteData: false, ct); }
                catch (Exception e) { Log.Warn($"Could not release torrent '{tag}': {e.Message}"); }
            }

        context.Remove(manga);
        await context.Sync(ct, GetType(), "Delete Series");
        return true;
    }

    public async Task<(ChangeLibraryStatus status, string? error)> ChangeLibraryAsync(
        SeriesContext context, ActionsContext actionsContext, string mangaId, string libraryId,
        string? connectorName, string? connectorSeriesId, bool download, CancellationToken ct)
    {
        if (await context.FileLibraries.FirstOrDefaultAsync(l => l.Key == libraryId, ct) is not { } library)
            return (ChangeLibraryStatus.LibraryNotFound, null);

        var manga = await context.Series
            .Include(m => m.Library)
            .Include(m => m.SourceIds)
            .Include(m => m.Chapters)
            .ThenInclude(c => c.SourceIds)
            .FirstOrDefaultAsync(m => m.Key == mangaId, ct);

        if (manga is null)
        {
            if (string.IsNullOrWhiteSpace(connectorName) || string.IsNullOrWhiteSpace(connectorSeriesId))
                return (ChangeLibraryStatus.SeriesNotFound, null);

            if (connectors.FirstOrDefault(c => c.Name.Equals(connectorName, StringComparison.InvariantCultureIgnoreCase)) is not { } connector)
                return (ChangeLibraryStatus.ConnectorNotFound, null);

            if (await connector.GetMangaFromId(connectorSeriesId) is not ({ } m, { } id))
                return (ChangeLibraryStatus.ConnectorSeriesNotFound, null);

            if (await context.UpsertManga(m, id, ct) is not { } added)
                return (ChangeLibraryStatus.SaveFailed, "Could not add Series to context");

            manga = added.manga;
        }

        manga.IsTracked = true;

        // Adding from a search result is one decision: with download=true the originating source is
        // enabled here and now (the source toggle remains the per-source control afterwards); either
        // way cover + chapter sync queue immediately so the series is never an empty shell.
        SourceId<Series>? addedFrom = connectorName is null ? null
            : manga.SourceIds.FirstOrDefault(id => id.MangaConnectorName.Equals(connectorName, StringComparison.InvariantCultureIgnoreCase));
        if (download && addedFrom is not null)
        {
            addedFrom.UseForDownload = true;
            foreach (SourceId<Chapter> chId in manga.Chapters.SelectMany(ch =>
                         ch.SourceIds.Where(chId => chId.MangaConnectorName.Equals(connectorName, StringComparison.InvariantCultureIgnoreCase))))
                chId.UseForDownload = true;
        }

        async Task EnqueueAddJobs()
        {
            if (addedFrom is not null)
                await SeriesJobs.EnqueueCoverAndSync(jobStore, clock, addedFrom, settings.DownloadLanguage, ct);
        }

        if (manga.LibraryId == library.Key)
        {
            await context.Sync(ct, GetType(), "Track Series");
            await EnqueueAddJobs();
            return (ChangeLibraryStatus.Ok, null);
        }

        Dictionary<Chapter, string?> oldPaths = manga.Chapters.Where(ch => ch.Downloaded).ToDictionary(ch => ch, ch => ch.FullArchiveFilePath);
        manga.Library = library;
        Dictionary<Chapter, string?> newPaths = oldPaths.ToDictionary(kv => kv.Key, kv => kv.Key.FullArchiveFilePath);
        foreach (var kv in oldPaths)
            await jobStore.EnqueueAsync(new Job(
                MoveDataHandler.Type,
                MoveDataHandler.PayloadFor(kv.Value!, newPaths[kv.Key]!), clock.UtcNow,
                dedupKey: MoveDataHandler.DedupKey(newPaths[kv.Key]!)), ct);

        if (await context.Sync(ct, GetType(), "Move Series") is { success: false } mangaContextResult)
            return (ChangeLibraryStatus.SaveFailed, mangaContextResult.exceptionMessage);

        await EnqueueAddJobs();

        actionsContext.Actions.Add(new LibraryMovedActionRecord(manga, library));
        if (await actionsContext.Sync(ct, GetType(), "Move Series") is { success: false } actionsContextResult)
            return (ChangeLibraryStatus.SaveFailed, actionsContextResult.exceptionMessage);

        return (ChangeLibraryStatus.Ok, null);
    }
}
