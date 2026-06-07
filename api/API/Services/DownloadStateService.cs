using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Reconciles each chapter's Downloaded flag with what is actually on disk (via
/// <see cref="Chapter.CheckDownloaded"/>) — the logic formerly in UpdateChaptersDownloadedWorker. A
/// chapter whose file is missing is flipped back to not-downloaded so the download runtime re-fetches it.
/// </summary>
public class DownloadStateService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DownloadStateService));

    public async Task VerifyAllAsync(SeriesContext context, KenkuSettings settings, CancellationToken ct)
    {
        Log.Debug("Checking chapter files...");
        List<Chapter> chapters = await context.Chapters.ToListAsync(ct);
        Log.DebugFormat("Checking {0} chapters...", chapters.Count);
        foreach (Chapter chapter in chapters)
        {
            try
            {
                bool downloaded = await chapter.CheckDownloaded(context, settings.ChapterNamingScheme, token: ct);
                chapter.Downloaded = downloaded;
                if (!downloaded)
                    chapter.FileName = null;
            }
            catch (Exception exception)
            {
                Log.Error(exception);
            }
        }

        if (await context.Sync(ct, typeof(DownloadStateService), nameof(VerifyAllAsync)) is { success: false } e)
            Log.ErrorFormat("Failed to save database changes: {0}", e.exceptionMessage);
    }
}
