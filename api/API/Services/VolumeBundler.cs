using System.IO.Compression;
using System.Text;
using API.Schema.SeriesContext;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Services;

/// <summary>
/// Bundles a VolumeCBZ volume's chapter files into a single <c>Vol N.cbz</c> (and the reverse). This is
/// the domain logic shared by the legacy bundle/unbundle workers and the ReconcileVolumeBundle job
/// handler, so both behave identically during the migration. The bundle is the source of truth: unbundle
/// reconstructs the original chapter files from it and the recorded <see cref="BundleChapterMap"/>.
/// </summary>
public class VolumeBundler(KenkuSettings settings)
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(VolumeBundler));

    /// <summary>
    /// Makes a volume's bundle correct: bundles it if it is ready and unbundled, rebuilds it if its
    /// chapter set has drifted from what the bundle records, and no-ops if it is already fresh or not yet
    /// ready. Idempotent — safe to re-run. Absorbs the EnsureReadyVolumesBundled / EnsureBundledVolumesFresh
    /// reconcilers into a single per-volume operation.
    /// </summary>
    public async Task ReconcileAsync(SeriesContext context, string mangaId, int volumeNumber, CancellationToken ct)
    {
        if (await context.Series.Include(m => m.Chapters).FirstOrDefaultAsync(m => m.Key == mangaId, ct) is not { } series)
        {
            Log.Warn($"Series not found for manga {mangaId}");
            return;
        }

        VolumeMetadata? volumeMetadata = await context.VolumeMetadata
            .FirstOrDefaultAsync(v => v.MangaId == mangaId && v.VolumeNumber == volumeNumber, ct);

        if (volumeMetadata?.ArchiveFileName is not null)
        {
            HashSet<string> recorded = (await context.BundleChapterMaps
                    .Where(m => m.VolumeKey == volumeMetadata.Key).ToListAsync(ct))
                .Select(m => m.ChapterKey).ToHashSet();
            HashSet<string> desired = series.Chapters
                .Where(c => c.VolumeNumber == volumeNumber && c.Downloaded).Select(c => c.Key).ToHashSet();

            if (desired.SetEquals(recorded))
                return; // bundle is fresh

            await UnbundleAsync(context, mangaId, volumeNumber, ct);
            await BundleAsync(context, mangaId, volumeNumber, ct);
            return;
        }

        if (VolumeBundlePolicy.VolumesReadyToBundle(series).Contains(volumeNumber))
            await BundleAsync(context, mangaId, volumeNumber, ct);
    }

    public async Task BundleAsync(SeriesContext context, string mangaId, int volumeNumber, CancellationToken ct)
    {
        var volumeMetadata = await context.VolumeMetadata
            .Include(v => v.Series)
            .ThenInclude(m => m.Library)
            .FirstOrDefaultAsync(v => v.MangaId == mangaId && v.VolumeNumber == volumeNumber, ct);

        Series manga;
        if (volumeMetadata is null)
        {
            // VolumeMetadata is a projection of Chapter.VolumeNumber; nothing creates it up front, so
            // derive it on demand from the manga. ArchiveFileName is filled in once the bundle is written.
            if (await context.Series.Include(m => m.Library)
                    .FirstOrDefaultAsync(m => m.Key == mangaId, ct) is not { } series)
            {
                Log.Warn($"Series not found for manga {mangaId}");
                return;
            }
            manga = series;
            volumeMetadata = new VolumeMetadata(manga, volumeNumber);
            context.VolumeMetadata.Add(volumeMetadata);
        }
        else
        {
            manga = volumeMetadata.Series;
        }

        var chapters = await context.Chapters
            .Where(c => c.ParentMangaId == mangaId
                        && c.VolumeNumber == volumeNumber
                        && !c.IsBundled
                        && c.FileName != null)
            .ToListAsync(ct);

        if (chapters.Count == 0)
        {
            Log.Info($"No unbundled chapters found for manga {mangaId} volume {volumeNumber}");
            return;
        }

        // Sort by ChapterNumber (parse as float for natural sort)
        chapters = chapters
            .OrderBy(c => float.TryParse(c.ChapterNumber, out float v) ? v : float.MaxValue)
            .ThenBy(c => c.ChapterNumber)
            .ToList();

        string outputPath = Path.Join(manga.FullDirectoryPath, $"Vol {volumeNumber}.cbz");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Track: chapter key → (original file path, startPage, pageCount)
        var chapterInfo = new List<(Chapter chapter, string originalPath, int startPage, int pageCount)>();
        int globalOffset = 0;

        try
        {
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var outputZip = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: false);

            foreach (var chapter in chapters)
            {
                string chapterPath = Path.Join(manga.FullDirectoryPath, chapter.FileName!);
                if (!File.Exists(chapterPath))
                {
                    Log.Warn($"Chapter file not found: {chapterPath}; skipping chapter {chapter.ChapterNumber}");
                    continue;
                }

                using var chapterZip = ZipFile.OpenRead(chapterPath);
                var imageEntries = chapterZip.Entries
                    .Where(e => IsImageEntry(e.FullName))
                    .OrderBy(e => e.Name, NaturalSortComparer.Instance)
                    .ToList();

                int startPage = globalOffset;
                int pageCount = imageEntries.Count;

                for (int i = 0; i < imageEntries.Count; i++)
                {
                    string ext = Path.GetExtension(imageEntries[i].Name).ToLowerInvariant();
                    string entryName = $"{globalOffset + i:D5}{ext}";
                    var newEntry = outputZip.CreateEntry(entryName);
                    using var entryStream = newEntry.Open();
                    using var sourceStream = imageEntries[i].Open();
                    await sourceStream.CopyToAsync(entryStream, ct);
                }

                chapterInfo.Add((chapter, chapterPath, startPage, pageCount));
                globalOffset += pageCount;
            }

            // Write ComicInfo.xml
            string comicInfoXml = BuildComicInfoXml(manga.Name, volumeNumber, globalOffset);
            var comicInfoEntry = outputZip.CreateEntry("ComicInfo.xml");
            using var comicInfoStream = comicInfoEntry.Open();
            var xmlBytes = Encoding.UTF8.GetBytes(comicInfoXml);
            await comicInfoStream.WriteAsync(xmlBytes, ct);
        }
        catch (Exception ex)
        {
            Log.Error($"Error building bundle for manga {mangaId} volume {volumeNumber}: {ex.Message}", ex);
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* best effort */ }
            }
            return;
        }

        // Write DB state
        foreach (var (chapter, _, startPage, pageCount) in chapterInfo)
        {
            context.BundleChapterMaps.Add(new BundleChapterMap
            {
                VolumeKey = volumeMetadata.Key,
                ChapterKey = chapter.Key,
                StartPage = startPage,
                PageCount = pageCount
            });
            chapter.IsBundled = true;
            chapter.FileName = null;
        }

        volumeMetadata.ArchiveFileName = Path.GetFileName(outputPath);

        await context.Sync(ct, typeof(VolumeBundler), nameof(BundleAsync));

        // Only after successful DB sync: delete original chapter files
        foreach (var (_, originalPath, _, _) in chapterInfo)
        {
            if (File.Exists(originalPath))
            {
                try
                {
                    File.Delete(originalPath);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not delete original chapter file '{originalPath}': {ex.Message}");
                }
            }
        }
    }

    public async Task UnbundleAsync(SeriesContext context, string mangaId, int volumeNumber, CancellationToken ct)
    {
        var volumeMetadata = await context.VolumeMetadata
            .Include(v => v.Series)
            .ThenInclude(m => m.Library)
            .FirstOrDefaultAsync(v => v.MangaId == mangaId && v.VolumeNumber == volumeNumber, ct);

        if (volumeMetadata is null)
        {
            Log.Warn($"VolumeMetadata not found for manga {mangaId} volume {volumeNumber}");
            return;
        }

        var maps = await context.BundleChapterMaps
            .Where(m => m.VolumeKey == volumeMetadata.Key)
            .OrderBy(m => m.StartPage)
            .ToListAsync(ct);

        if (maps.Count == 0)
        {
            Log.Warn($"No BundleChapterMap rows found for volume {volumeNumber}; nothing to unbundle.");
            return;
        }

        var manga = volumeMetadata.Series;

        if (volumeMetadata.ArchiveFileName is null)
        {
            Log.Warn($"Volume {volumeNumber} has no ArchiveFileName; cannot unbundle.");
            return;
        }

        string bundlePath = Path.Join(manga.FullDirectoryPath, volumeMetadata.ArchiveFileName);
        if (!File.Exists(bundlePath))
        {
            Log.Warn($"Bundle file not found at {bundlePath}");
            return;
        }

        var chapterKeys = maps.Select(m => m.ChapterKey).ToHashSet();
        var chapters = await context.Chapters
            .Include(c => c.ParentManga)
            .ThenInclude(m => m.Library)
            .Where(c => chapterKeys.Contains(c.Key))
            .ToListAsync(ct);

        var chapterByKey = chapters.ToDictionary(c => c.Key);
        var extractedPaths = new List<string>();

        try
        {
            using var bundleZip = ZipFile.OpenRead(bundlePath);

            var allImageEntries = bundleZip.Entries
                .Where(e => IsImageEntry(e.FullName))
                .OrderBy(e => e.Name, NaturalSortComparer.Instance)
                .ToList();

            foreach (var map in maps)
            {
                if (!chapterByKey.TryGetValue(map.ChapterKey, out var chapter))
                {
                    Log.Warn($"Chapter {map.ChapterKey} not found in DB; skipping.");
                    continue;
                }

                string outputFileName = chapter.GetArchiveFileName(settings.ChapterNamingScheme);
                string outputPath = Path.Join(manga.FullDirectoryPath, outputFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                var pageEntries = allImageEntries
                    .Skip(map.StartPage)
                    .Take(map.PageCount)
                    .ToList();

                using var chapterStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                using var chapterZip = new ZipArchive(chapterStream, ZipArchiveMode.Create, leaveOpen: false);

                foreach (var entry in pageEntries)
                {
                    var newEntry = chapterZip.CreateEntry(entry.Name);
                    using var destStream = newEntry.Open();
                    using var srcStream = entry.Open();
                    await srcStream.CopyToAsync(destStream, ct);
                }

                chapter.IsBundled = false;
                chapter.FileName = outputFileName;
                extractedPaths.Add(outputPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error unbundling volume {volumeNumber} for manga {mangaId}: {ex.Message}", ex);
            return;
        }

        volumeMetadata.ArchiveFileName = null;
        context.BundleChapterMaps.RemoveRange(maps);

        await context.Sync(ct, typeof(VolumeBundler), nameof(UnbundleAsync));

        // Only after successful DB sync: delete bundle file
        if (File.Exists(bundlePath))
        {
            try
            {
                File.Delete(bundlePath);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not delete bundle file '{bundlePath}': {ex.Message}");
            }
        }
    }

    private static bool IsImageEntry(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") ||
               lower.EndsWith(".png") || lower.EndsWith(".webp");
    }

    private static string BuildComicInfoXml(string mangaName, int vol, int pageCount)
    {
        return $"""
                <?xml version="1.0"?>
                <ComicInfo>
                  <Series>{EscapeXml(mangaName)}</Series>
                  <Volume>{vol}</Volume>
                  <PageCount>{pageCount}</PageCount>
                </ComicInfo>
                """;
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
