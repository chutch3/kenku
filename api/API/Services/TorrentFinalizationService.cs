using API.DownloadClients;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Finalises a completed torrent download: finds the .cbz in the torrent's save directory, moves it into
/// the chapter's publication folder, marks the chapter downloaded, and hands the torrent back to the
/// client for removal — the logic formerly in TorrentCompletionWorker.FinaliseAsync. Idempotent: a
/// chapter already marked downloaded is a no-op.
/// </summary>
public class TorrentFinalizationService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorrentFinalizationService));

    public async Task FinalizeAsync(SeriesContext seriesContext, ActionsContext actionsContext,
        IDownloadClient downloadClient, KenkuSettings settings, string sourceIdKey, string savePath, CancellationToken ct)
    {
        SourceId<Chapter>? chId = await seriesContext.MangaConnectorToChapter
            .Include(id => id.Obj).ThenInclude(c => c.ParentManga).ThenInclude(m => m.Library)
            .FirstOrDefaultAsync(id => id.Key == sourceIdKey, ct);
        if (chId is null)
        {
            Log.ErrorFormat("Could not finalise torrent: SourceId {0} not found.", sourceIdKey);
            return;
        }

        Chapter chapter = chId.Obj;
        if (chapter.Downloaded)
            return;

        if (chapter.ParentManga.LibraryId is null)
        {
            Log.WarnFormat("Torrent for {0} completed but chapter has no library assigned; skipping.", chapter);
            return;
        }

        if (!Directory.Exists(savePath))
        {
            Log.ErrorFormat("Torrent reports completion at {0} but the directory does not exist.", savePath);
            return;
        }

        string? archive = Directory.EnumerateFiles(savePath, "*.cbz", SearchOption.AllDirectories).FirstOrDefault();
        if (archive is null)
        {
            Log.ErrorFormat("Torrent completed at {0} but contains no .cbz; cannot finalise {1}.", savePath, chapter);
            return;
        }

        string? targetPath = chapter.GetFullFilepath(settings.ChapterNamingScheme);
        if (targetPath is null)
        {
            Log.ErrorFormat("Could not resolve a target path for {0}; cannot finalise.", chapter);
            return;
        }

        string? targetDir = Path.GetDirectoryName(targetPath);
        if (targetDir != null && !Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        try
        {
            File.Move(archive, targetPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to move {0} → {1}: {2}", archive, targetPath, ex);
            return;
        }

        chapter.Downloaded = true;
        chapter.FileName = new FileInfo(targetPath).Name;

        actionsContext.Actions.Add(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter));

        var syncs = await Task.WhenAll(
            seriesContext.Sync(ct, typeof(TorrentFinalizationService), nameof(FinalizeAsync)),
            actionsContext.Sync(ct, typeof(TorrentFinalizationService), nameof(FinalizeAsync)));
        foreach (var s in syncs)
            if (!s.success) Log.ErrorFormat("Sync failed during torrent finalise: {0}", s.exceptionMessage);

        // Hand the torrent back to the client for removal; keep the seeded data on disk in case the user
        // has ratio targets. The .cbz itself we already moved out.
        await downloadClient.Remove(sourceIdKey, deleteData: false, ct);

        Log.InfoFormat("Finalised torrent for {0} ch.{1} → {2}", chapter.ParentManga.Name, chapter.ChapterNumber, targetPath);
    }
}
