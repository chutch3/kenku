using API.DownloadClients.Interfaces;
using API.DownloadClients;
using API.Indexers;
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

        string[] archives = Directory.EnumerateFiles(savePath, "*.cbz", SearchOption.AllDirectories).ToArray();
        if (archives.Length == 0)
        {
            Log.ErrorFormat("Torrent completed at {0} but contains no .cbz; cannot finalise {1}.", savePath, chapter);
            return;
        }

        // A single archive is what the release was selected for — trust the selection. Multiple
        // archives mean a pack: fan each file out to the chapter its filename parses to, so one
        // pack download fulfils the whole run (other chapters' pending jobs no-op on Downloaded).
        int placed;
        if (archives.Length == 1)
        {
            placed = Place(archives[0], chapter, settings, actionsContext) ? 1 : 0;
        }
        else
        {
            List<Chapter> seriesChapters = await seriesContext.Chapters
                .Where(c => c.ParentMangaId == chapter.ParentMangaId && !c.Downloaded)
                .ToListAsync(ct);
            placed = 0;
            foreach (string archive in archives)
            {
                ParsedRelease parsed = ReleaseTitleParser.Parse(Path.GetFileNameWithoutExtension(archive));
                // Same-series check keeps a pack's extras/specials from claiming a main-run issue number.
                if (parsed.IssueNumber is null) continue;
                if (!string.Equals(parsed.SeriesTitle, chapter.ParentManga.Name, StringComparison.OrdinalIgnoreCase)) continue;
                Chapter? target = seriesChapters.FirstOrDefault(c => c.ChapterNumber == parsed.IssueNumber && !c.Downloaded);
                if (target is null) continue;
                if (Place(archive, target, settings, actionsContext)) placed++;
            }
        }

        if (placed == 0)
        {
            Log.ErrorFormat("Torrent at {0} contained {1} archive(s) but none could be placed for {2}; leaving it for inspection.",
                savePath, archives.Length, chapter);
            return;
        }

        var syncs = await Task.WhenAll(
            seriesContext.Sync(ct, typeof(TorrentFinalizationService), nameof(FinalizeAsync)),
            actionsContext.Sync(ct, typeof(TorrentFinalizationService), nameof(FinalizeAsync)));
        foreach (var s in syncs)
            if (!s.success) Log.ErrorFormat("Sync failed during torrent finalise: {0}", s.exceptionMessage);

        // Hand the torrent back to the client for removal; keep the seeded data on disk in case the user
        // has ratio targets. The .cbz files themselves we already moved out.
        await downloadClient.Remove(sourceIdKey, deleteData: false, ct);

        Log.InfoFormat("Finalised torrent for {0}: placed {1} chapter file(s).", chapter.ParentManga.Name, placed);
    }

    /// <summary>Moves one archive into <paramref name="chapter"/>'s publication path and marks it downloaded.</summary>
    private static bool Place(string archive, Chapter chapter, KenkuSettings settings, ActionsContext actionsContext)
    {
        string? targetPath = chapter.GetFullFilepath(settings.ChapterNamingScheme);
        if (targetPath is null)
        {
            Log.ErrorFormat("Could not resolve a target path for {0}; cannot place {1}.", chapter, archive);
            return false;
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
            return false;
        }

        chapter.Downloaded = true;
        chapter.FileName = new FileInfo(targetPath).Name;
        actionsContext.Actions.Add(new ChapterDownloadedActionRecord(chapter.ParentManga, chapter));
        return true;
    }
}
