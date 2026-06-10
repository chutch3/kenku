using System.Text.RegularExpressions;
using API.HttpRequesters;
using API.HttpRequesters.Interfaces;
using API.Schema.SeriesContext;
using log4net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace API.Services;

/// <summary>
/// Fetches a series cover and writes the original plus the three resized variants into the cover
/// cache. Owns the image pipeline that used to live on the SeriesSource base class — connectors
/// delegate here, supplying their own HTTP edge.
/// </summary>
public static class CoverImageCache
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(CoverImageCache));

    public static async Task<string?> SaveAsync(KenkuSettings settings, IHttpRequester http, SourceId<Series> mangaId, int retries = 3)
    {
        if (retries < 0)
            return null;

        Regex urlRex = new(@"https?:\/\/((?:[a-zA-Z0-9-]+\.)+[a-zA-Z0-9]+)\/(?:.+\/)*(.+\.([a-zA-Z]+))");
        //https?:\/\/[a-zA-Z0-9-]+\.([a-zA-Z0-9-]+\.[a-zA-Z0-9]+)\/(?:.+\/)*(.+\.([a-zA-Z]+)) for only second level domains
        Match match = urlRex.Match(mangaId.Obj.CoverUrl);
        // Clean ONCE up front so the file written to disk and the value returned are always identical.
        string filename = $"{match.Groups[1].Value}-{mangaId.ObjId}.{mangaId.MangaConnectorName}.{match.Groups[3].Value}".CleanNameForWindows();
        string saveImagePath = Path.Join(settings.CoverImageCacheOriginal, filename);

        if (File.Exists(saveImagePath))
            return filename;

        using HttpResponseMessage coverResult = await http.MakeRequest(mangaId.Obj.CoverUrl, RequestType.MangaCover, $"https://{match.Groups[1].Value}");
        if ((int)coverResult.StatusCode < 200 || (int)coverResult.StatusCode >= 300)
            return await SaveAsync(settings, http, mangaId, retries - 1);

        try
        {
            using MemoryStream ms = new();
            await (await coverResult.Content.ReadAsStreamAsync()).CopyToAsync(ms);
            ms.Position = 0;
            Directory.CreateDirectory(settings.CoverImageCacheOriginal);
            File.WriteAllBytes(saveImagePath, ms.ToArray());

            using Image image = await Image.LoadAsync(ms); // Use stream for async load
            Directory.CreateDirectory(settings.CoverImageCacheLarge);
            using Image large = image.Clone(x => x.Resize(new ResizeOptions
                { Size = Constants.ImageLgSize, Mode = ResizeMode.Max }));
            large.SaveAsJpeg(Path.Join(settings.CoverImageCacheLarge, filename), new (){ Quality = 40 });

            Directory.CreateDirectory(settings.CoverImageCacheMedium);
            using Image medium = image.Clone(x => x.Resize(new ResizeOptions
                { Size = Constants.ImageMdSize, Mode = ResizeMode.Max }));
            medium.SaveAsJpeg(Path.Join(settings.CoverImageCacheMedium, filename), new (){ Quality = 40 });

            Directory.CreateDirectory(settings.CoverImageCacheSmall);
            using Image small = image.Clone(x => x.Resize(new ResizeOptions
                { Size = Constants.ImageSmSize, Mode = ResizeMode.Max }));
            small.SaveAsJpeg(Path.Join(settings.CoverImageCacheSmall, filename), new (){ Quality = 40 });
        }
        catch (Exception e)
        {
            Log.Error(e);
        }

        return filename;
    }
}
