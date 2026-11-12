using API.Acquirers.Interfaces;
using System.IO.Compression;
using System.Text;
using API.Connectors;
using API.Schema.SeriesContext;
using log4net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Binarization;

namespace API.Acquirers;

/// <summary>
/// Acquires a chapter by fetching each page image individually from the connector, optionally
/// re-encoding them (compression / black-and-white), and packaging the lot into a .cbz with a
/// ComicInfo.xml. Pages the source can't deliver — a "missing page" placeholder, or an undownloadable/
/// undecodable image — are detected (<see cref="MissingPageDetector"/>) and dropped; the chapter is still
/// saved but reports the dropped count via <see cref="AcquireResult.Acquired.MissingPages"/> so it can be
/// flagged and force-rebuilt later.
/// </summary>
public class ImageListAcquirer(KenkuSettings settings) : IChapterAcquirer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ImageListAcquirer));

    public AcquisitionKind Kind => AcquisitionKind.ImageList;

    public async Task<AcquireResult> AcquireAsync(
        SourceId<Chapter> chapter,
        SeriesSource source,
        string saveArchiveFilePath,
        CancellationToken ct,
        string? pinnedArchiveUrl = null)
    {
        string[] imageUrls;
        try
        {
            imageUrls = await source.GetChapterImageUrls(chapter);
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to resolve image URLs for chapter {0}: {1}", chapter.Obj, ex);
            return new AcquireResult.Failed($"could not resolve the chapter's page images: {ex.Message}");
        }

        if (imageUrls.Length == 0)
        {
            // No pages resolved (e.g. connector returned nothing). Fail instead of writing an
            // empty .cbz that would be marked Downloaded and never retried.
            Log.Warn($"No image URLs for chapter {chapter.Obj}; not writing an archive.");
            return new AcquireResult.Failed("the connector returned no page images for this chapter");
        }

        // Build the archive at a sibling temp path and move it into place only once it is complete, so a
        // cancel or crash mid-write can never leave a partial/corrupt .cbz at the final path (#31).
        string tempPath = saveArchiveFilePath + ".part";
        try
        {
            // Download and measure every page up front: a "missing page" placeholder is only detectable
            // relative to the chapter's real pages (see MissingPageDetector), so we need all the page
            // sizes before deciding which to keep. A page that won't download or decode gets area 0 and
            // is treated as missing too.
            var pages = new List<(byte[] bytes, long area)>(imageUrls.Length);
            foreach (string imageUrl in imageUrls)
            {
                Stream? imageStream = await source.DownloadImage(imageUrl, ct);
                if (imageStream is null)
                {
                    pages.Add(([], 0));
                    continue;
                }
                using MemoryStream buffered = new();
                await imageStream.CopyToAsync(buffered, ct);
                await imageStream.DisposeAsync();
                byte[] bytes = buffered.ToArray();
                pages.Add((bytes, PageArea(bytes)));
            }

            var missing = MissingPageDetector.Detect(pages.Select(p => p.area).ToList()).ToHashSet();

            int written = 0;
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                if (Constants.CreateComicInfoXml)
                {
                    Log.Debug("Writing ComicInfo.xml");
                    await using Stream comicStream = archive.CreateEntry("ComicInfo.xml").Open();
                    await comicStream.WriteAsync(Encoding.UTF8.GetBytes(chapter.Obj.GetComicInfoXmlString()), ct);
                }

                for (int i = 0; i < pages.Count; i++)
                {
                    if (missing.Contains(i))
                        continue; // drop the placeholder / undownloadable page; the count is reported below

                    await using MemoryStream pageStream = new(pages[i].bytes);
                    await using Stream processed = await ProcessImage(pageStream, ct);
                    processed.Position = 0;
                    await using Stream zipStream = archive.CreateEntry($"{written}.jpg").Open();
                    await processed.CopyToAsync(zipStream, ct);
                    written++;
                }
            }

            if (written == 0)
            {
                Log.Warn($"None of the {imageUrls.Length} page image(s) for chapter {chapter.Obj} were usable; not writing an archive.");
                TryDelete(tempPath);
                return new AcquireResult.Failed($"none of the {imageUrls.Length} page image(s) could be downloaded");
            }

            if (missing.Count > 0)
                Log.WarnFormat("Chapter {0} saved with {1} of {2} page(s) missing (placeholder/undownloadable).",
                    chapter.Obj, missing.Count, imageUrls.Length);

            File.Move(tempPath, saveArchiveFilePath, overwrite: true);
            return new AcquireResult.Acquired(saveArchiveFilePath, missing.Count);
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to download chapter {0}: {1}", chapter.Obj, ex);
            TryDelete(tempPath);
            return new AcquireResult.Failed($"chapter download failed: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Log.Warn($"Could not delete temp archive {path}: {ex.Message}"); }
    }

    /// <summary>The page's pixel area (header-only read), or 0 if the bytes don't decode as an image.</summary>
    private static long PageArea(byte[] bytes)
    {
        try
        {
            var info = Image.Identify(new MemoryStream(bytes));
            return (long)info.Width * info.Height;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<Stream> ProcessImage(Stream imageStream, CancellationToken cancellationToken)
    {
        Log.Debug("Processing image");
        imageStream.Position = 0;
        if (!settings.BlackWhiteImages && settings.ImageCompression == 100)
        {
            Log.Debug("No processing requested for image");
            // No new stream is created; the caller still owns and disposes imageStream.
            return imageStream;
        }

        MemoryStream processedImage = new();
        try
        {
            using Image image = await Image.LoadAsync(imageStream, cancellationToken);
            Log.Debug("Image loaded");
            if (settings.BlackWhiteImages)
                image.Mutate(i => i.ApplyProcessor(new AdaptiveThresholdProcessor()));
            await image.SaveAsJpegAsync(processedImage, new JpegEncoder
            {
                Quality = settings.ImageCompression
            }, cancellationToken);
            Log.Debug("Image processed");
        }
        catch (Exception e)
        {
            Log.Error(e);
            // Processing failed: fall back to the raw source stream (caller disposes it).
            await processedImage.DisposeAsync();
            return imageStream;
        }
        // Processing succeeded: a new stream now holds the image data, so dispose the source to avoid
        // leaking the underlying network stream/handle.
        await imageStream.DisposeAsync();
        processedImage.Position = 0;
        return processedImage;
    }
}
