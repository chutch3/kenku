using API.HttpRequesters.Interfaces;
using API.Acquirers;
using API.Connectors;
using API.Schema.SeriesContext;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace API.Tests;

/// <summary>
/// A configurable stand-in connector: pick its name and acquisition kind, optionally give it a real
/// HTTP edge (for cover-cache tests). Every connector method throws — tests that need behaviour use
/// Mock&lt;SeriesSource&gt; instead.
/// </summary>
public sealed class FakeSeriesSource : SeriesSource
{
    private readonly AcquisitionKind _kind;
    private readonly ContentType _contentType;

    public FakeSeriesSource(string name, KenkuSettings settings,
        AcquisitionKind kind = AcquisitionKind.ImageList, IHttpRequester? httpRequester = null,
        ContentType contentType = ContentType.Manga)
        : base(name, ["en"], ["fake.test"], "icon", settings)
    {
        _kind = kind;
        _contentType = contentType;
        if (httpRequester is not null)
            downloadClient = httpRequester;
    }

    public override AcquisitionKind Kind => _kind;
    public override ContentType ContentType => _contentType;
    public override Task<(Series, SourceId<Series>)[]> SearchManga(string mangaSearchName) => throw new NotSupportedException();
    public override Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
    public override Task<(Series, SourceId<Series>)?> GetMangaFromId(string mangaIdOnSite) => throw new NotSupportedException();
    public override Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> mangaId, string? language = null) => throw new NotSupportedException();
    internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId) => throw new NotSupportedException();
}

/// <summary>Shared tiny binary fixtures.</summary>
public static class TestImages
{
    /// <summary>A valid 8×8 JPEG — small enough for any test, real enough for ImageSharp.</summary>
    public static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }
}
