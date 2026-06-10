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
/// added series is never an empty shell.
/// </summary>
public class SeriesLibraryService(KenkuSettings settings, IEnumerable<SeriesSource> connectors, IJobStore jobStore, IClock clock)
{
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
