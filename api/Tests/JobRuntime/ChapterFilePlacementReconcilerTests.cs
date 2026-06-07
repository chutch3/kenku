using API.JobRuntime.Reconcilers;
using API;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The reconciler that replaced SyncChapterFileNames: it enqueues a PlaceChapterFile job per downloaded
/// chapter whose stored filename has drifted from the current naming scheme / layout, deduped per chapter.
/// </summary>
public class ChapterFilePlacementReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-place-" + Guid.NewGuid().ToString("N"));
    private const string NamingScheme = "?V(%M Vol %V/)%M - Ch.%C";

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("place-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private async Task<(SeriesContext ctx, Series manga)> SeedSeries(string name = "One-Punch Man")
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var manga = new Series(name, "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        ctx.Series.Add(manga);
        await ctx.SaveChangesAsync();
        return (ctx, manga);
    }

    private static KenkuSettings Settings => new() { ChapterNamingScheme = NamingScheme };

    private static Task<int> Scan(SeriesContext ctx, InMemoryJobStore store) =>
        ChapterFilePlacementReconciler.ScanAndEnqueueAsync(ctx, store, Settings, DateTime.UtcNow, default);

    [Fact]
    public async Task Scan_WhenFileNameDoesNotMatch_EnqueuesPlacementJob()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.1.cbz" });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(PlaceChapterFileHandler.Type, job.Type);
    }

    [Fact]
    public async Task Scan_WhenFileNameAlreadyMatchesNamingScheme_EnqueuesNothing()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null)
            { Downloaded = true, FileName = "One-Punch Man Vol 5/One-Punch Man - Ch.1.cbz" });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_WhenChapterNotDownloaded_SkipsChapter()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = false, FileName = "One-Punch Man - Ch.1.cbz" });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_WhenChapterHasNullFileName_SkipsChapter()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = true, FileName = null });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_WhenMultipleChaptersMismatched_EnqueuesOneJobEach()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.1.cbz" });
        ctx.Chapters.Add(new Chapter(manga, "2", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.2.cbz" });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);

        Assert.Equal(2, (await store.GetAllAsync()).Count);
    }

    [Fact]
    public async Task Scan_IsDedupedPerChapter_SoTicksDoNotPileUp()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.Chapters.Add(new Chapter(manga, "1", 5, null) { Downloaded = true, FileName = "One-Punch Man - Ch.1.cbz" });
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await Scan(ctx, store);
        await Scan(ctx, store);

        Assert.Single(await store.GetAllAsync());
    }
}
