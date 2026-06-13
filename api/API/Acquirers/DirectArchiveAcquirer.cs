using API.Acquirers.Interfaces;
using API.Connectors;
using API.Schema.SeriesContext;
using log4net;

namespace API.Acquirers;

/// <summary>
/// Acquires a chapter by downloading a single already-packaged archive (.cbz/.cbr) from
/// <see cref="SourceId{T}.WebsiteUrl"/>. Connectors whose Kind is <see cref="AcquisitionKind.DirectArchive"/>
/// are contractually responsible for populating <c>WebsiteUrl</c> with the archive URL itself,
/// not a viewer/page URL.
/// </summary>
public class DirectArchiveAcquirer(HttpClient http) : IChapterAcquirer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DirectArchiveAcquirer));

    public AcquisitionKind Kind => AcquisitionKind.DirectArchive;

    public async Task<AcquireResult> AcquireAsync(
        SourceId<Chapter> chapter,
        SeriesSource source,
        string saveArchiveFilePath,
        CancellationToken ct,
        string? pinnedArchiveUrl = null)
    {
        if (string.IsNullOrWhiteSpace(chapter.WebsiteUrl) && pinnedArchiveUrl is null)
        {
            Log.ErrorFormat("Cannot acquire chapter {0}: WebsiteUrl is empty (a DirectArchive connector must populate it with the archive URL).", chapter);
            return new AcquireResult.Failed("the connector did not provide an archive URL for this chapter");
        }

        string archiveUrl = pinnedArchiveUrl ?? chapter.WebsiteUrl!;
        if (pinnedArchiveUrl is null && source is IArchiveUrlResolver resolver)
        {
            switch (await resolver.ResolveArchiveUrl(chapter, ct))
            {
                case ArchiveResolution.Resolved resolved:
                    archiveUrl = resolved.Url;
                    break;
                case ArchiveResolution.Manual manual:
                    Log.InfoFormat("Chapter {0} needs manual handling: {1}", chapter, manual.Reason);
                    return new AcquireResult.Failed(manual.Reason);
                case ArchiveResolution.Choice choice:
                    Log.InfoFormat("Chapter {0} offers {1} downloads; the user picks one.", chapter, choice.Options.Count);
                    return new AcquireResult.Failed(
                        $"the post offers {choice.Options.Count} downloads — choose one from the failed job in Activity");
            }
        }

        try
        {
            using HttpResponseMessage response = await http.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                Log.ErrorFormat("Failed to download archive {0}: HTTP {1}", archiveUrl, (int)response.StatusCode);
                return new AcquireResult.Failed($"archive download failed: HTTP {(int)response.StatusCode}");
            }

            await using FileStream fs = File.Create(saveArchiveFilePath);
            await response.Content.CopyToAsync(fs, ct);
            return new AcquireResult.Acquired(saveArchiveFilePath);
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to acquire archive {0}: {1}", archiveUrl, ex);
            // Best effort cleanup of any partial file
            try { if (File.Exists(saveArchiveFilePath)) File.Delete(saveArchiveFilePath); } catch { /* swallow */ }
            return new AcquireResult.Failed($"archive download failed: {ex.Message}");
        }
    }
}
