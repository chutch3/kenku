using System.IO.Compression;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.Unit.Services;

public class VolumeBundlerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-bundler-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static byte[] FakeCbz(int pages)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            for (int i = 1; i <= pages; i++)
                using (zip.CreateEntry($"{i:D3}.jpg").Open()) { }
        return ms.ToArray();
    }

    private static int ImageCount(string cbz)
    {
        using var zip = ZipFile.OpenRead(cbz);
        return zip.Entries.Count(e => e.FullName.EndsWith(".jpg"));
    }

    private (SeriesContext ctx, KenkuSettings settings, string seriesKey, string mangaDir) Seed(Action<SeriesContext, Series, string> seed)
    {
        Directory.CreateDirectory(_root);
        var settings = new KenkuSettings { AppData = _root, ChapterNamingScheme = "%M - Ch.%C" };
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("bundler-" + Guid.NewGuid().ToString("N")).Options;
        var ctx = new SeriesContext(options);

        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing,
            [], [], [], [], library) { LibraryLayout = LibraryLayout.VolumeCBZ };
        ctx.Series.Add(manga);

        string dir = manga.FullDirectoryPath;
        Directory.CreateDirectory(dir);
        seed(ctx, manga, dir);
        ctx.SaveChanges();
        return (ctx, settings, manga.Key, dir);
    }

    [Fact]
    public async Task ReconcileAsync_ReadyClosedVolume_IsBundled()
    {
        var (ctx, settings, key, dir) = Seed((c, manga, d) =>
        {
            for (int i = 1; i <= 2; i++)
            {
                c.Chapters.Add(new Chapter(manga, i.ToString(), 1, null) { Downloaded = true, FileName = $"ch{i}.cbz" });
                File.WriteAllBytes(Path.Combine(d, $"ch{i}.cbz"), FakeCbz(3));
            }
            c.Chapters.Add(new Chapter(manga, "3", 2, null) { Downloaded = false }); // vol 2 → vol 1 is closed
        });

        await new VolumeBundler(settings).ReconcileAsync(ctx, key, 1, CancellationToken.None);

        string bundle = Path.Combine(dir, "Vol 1.cbz");
        Assert.True(File.Exists(bundle));
        Assert.Equal(6, ImageCount(bundle));
        Assert.Equal(2, await ctx.BundleChapterMaps.CountAsync());
    }

    [Fact]
    public async Task ReconcileAsync_FreshBundle_IsANoOp()
    {
        var (ctx, settings, key, dir) = Seed((c, manga, d) =>
        {
            for (int i = 1; i <= 2; i++)
            {
                c.Chapters.Add(new Chapter(manga, i.ToString(), 1, null) { Downloaded = true, FileName = $"ch{i}.cbz" });
                File.WriteAllBytes(Path.Combine(d, $"ch{i}.cbz"), FakeCbz(3));
            }
            c.Chapters.Add(new Chapter(manga, "3", 2, null) { Downloaded = false });
        });
        var bundler = new VolumeBundler(settings);
        await bundler.ReconcileAsync(ctx, key, 1, CancellationToken.None);
        DateTime firstWrite = File.GetLastWriteTimeUtc(Path.Combine(dir, "Vol 1.cbz"));

        await bundler.ReconcileAsync(ctx, key, 1, CancellationToken.None);

        Assert.Equal(2, await ctx.BundleChapterMaps.CountAsync());
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(Path.Combine(dir, "Vol 1.cbz")));
    }

    [Fact]
    public async Task ReconcileAsync_StaleBundle_IsRebuiltWithTheNewChapter()
    {
        var (ctx, settings, key, dir) = Seed((c, manga, d) =>
        {
            for (int i = 1; i <= 2; i++)
            {
                c.Chapters.Add(new Chapter(manga, i.ToString(), 1, null) { Downloaded = true, FileName = $"ch{i}.cbz" });
                File.WriteAllBytes(Path.Combine(d, $"ch{i}.cbz"), FakeCbz(3));
            }
            c.Chapters.Add(new Chapter(manga, "9", 2, null) { Downloaded = false });
        });
        var bundler = new VolumeBundler(settings);
        await bundler.ReconcileAsync(ctx, key, 1, CancellationToken.None);

        // A late chapter joins volume 1 after it was bundled.
        var manga = await ctx.Series.FirstAsync(m => m.Key == key);
        ctx.Chapters.Add(new Chapter(manga, "3", 1, null) { Downloaded = true, FileName = "ch3.cbz" });
        File.WriteAllBytes(Path.Combine(dir, "ch3.cbz"), FakeCbz(4));
        await ctx.SaveChangesAsync();

        await bundler.ReconcileAsync(ctx, key, 1, CancellationToken.None);

        string bundle = Path.Combine(dir, "Vol 1.cbz");
        Assert.True(File.Exists(bundle));
        Assert.Equal(3, await ctx.BundleChapterMaps.CountAsync());
        Assert.Equal(10, ImageCount(bundle)); // 3 + 3 + 4
    }
}
