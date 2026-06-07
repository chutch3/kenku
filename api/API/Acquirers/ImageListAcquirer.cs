using API.Acquirers.Interfaces;
using System.IO.Compression;
using System.Text;
using API.MangaConnectors;
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
/// ComicInfo.xml. This is the historical Kenku path — preserved verbatim from
/// DownloadChapterFromSourceWorker so the rename is a behaviour-preserving refactor.
/// </summary>
public class ImageListAcquirer(KenkuSettings settings) : IChapterAcquirer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(ImageListAcquirer));

    public AcquisitionKind Kind => AcquisitionKind.ImageList;

    public async Task<string?> AcquireAsync(
        SourceId<Chapter> chapter,
        SeriesSource source,
        string saveArchiveFilePath,
        CancellationToken ct)
    {
        string[] imageUrls;
        try
        {
            imageUrls = await source.GetChapterImageUrls(chapter);
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to resolve image URLs for chapter {0}: {1}", chapter.Obj, ex);
            return null;
        }

        if (imageUrls.Length == 0)
        {
            // No pages resolved (e.g. connector returned nothing). Fail instead of writing an
            // empty .cbz that would be marked Downloaded and never retried.
            Log.Warn($"No image URLs for chapter {chapter.Obj}; not writing an archive.");
            return null;
        }

        // Build the archive at a sibling temp path and move it into place only once it is complete, so a
        // cancel or crash mid-write can never leave a partial/corrupt .cbz at the final path (#31).
        string tempPath = saveArchiveFilePath + ".part";
        try
        {
            int written = 0;
            using (ZipArchive archive = ZipFile.Open(tempPath, ZipArchiveMode.Create))
            {
                if (Constants.CreateComicInfoXml)
                {
                    Log.Debug("Writing ComicInfo.xml");
                    await using Stream comicStream = archive.CreateEntry("ComicInfo.xml").Open();
                    await comicStream.WriteAsync(Encoding.UTF8.GetBytes(chapter.Obj.GetComicInfoXmlString()), ct);
                }

                foreach (string imageUrl in imageUrls)
                {
                    Stream? imageStream = await source.DownloadImage(imageUrl, ct);
                    if (imageStream is null)
                        continue;

                    await using Stream processed = await ProcessImage(imageStream, ct);
                    processed.Position = 0;
                    await using Stream zipStream = archive.CreateEntry($"{written}.jpg").Open();
                    await processed.CopyToAsync(zipStream, ct);
                    written++;
                }
            }

            if (written == 0)
            {
                Log.Warn($"None of the {imageUrls.Length} page image(s) for chapter {chapter.Obj} could be downloaded; not writing an archive.");
                TryDelete(tempPath);
                return null;
            }

            File.Move(tempPath, saveArchiveFilePath, overwrite: true);
            return saveArchiveFilePath;
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Failed to download chapter {0}: {1}", chapter.Obj, ex);
            TryDelete(tempPath);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Log.Warn($"Could not delete temp archive {path}: {ex.Message}"); }
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
