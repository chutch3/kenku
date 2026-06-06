using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The download reconciler that replaced StartNewChapterDownloads: it enqueues a DownloadChapter job per
/// wanted-but-missing chapter, deduped per source-id and keyed on the series — so a chapter is never
/// double-queued and one series can't hog the pool (the #31 loop is closed, not just slowed).
/// </summary>
public class DownloadReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-dlr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("dlr-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private async Task<(SeriesContext ctx, Series manga)> SeedSeries()
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var manga = new Series("Test Series", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        ctx.Series.Add(manga);
        await ctx.SaveChangesAsync();
        return (ctx, manga);
    }

    private static void AddChapter(SeriesContext ctx, Series manga, string number, bool downloaded = false,
        bool useForDownload = true, bool bundled = false)
    {
        var chapter = new Chapter(manga, number, null, null) { Downloaded = downloaded, IsBundled = bundled };
        ctx.Chapters.Add(chapter);
        ctx.MangaConnectorToChapter.Add(new SourceId<Chapter>(chapter, "MockConnector", $"site{number}", $"url{number}", useForDownload));
    }

    [Fact]
    public async Task Scan_EnqueuesADownloadJobPerMissingChapter_KeyedOnTheSeries()
    {
        var (ctx, manga) = await SeedSeries();
        AddChapter(ctx, manga, "1");
        AddChapter(ctx, manga, "2");
        AddChapter(ctx, manga, "3", downloaded: true);          // already downloaded → skip
        AddChapter(ctx, manga, "4", useForDownload: false);     // not wanted → skip
        AddChapter(ctx, manga, "5", bundled: true);             // bundled → skip
        await ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        int enqueued = await DownloadReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Equal(2, enqueued);
        var jobs = await store.GetAllAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(DownloadChapterHandler.Type, j.Type));
        Assert.All(jobs, j => Assert.Equal(manga.Key, j.ResourceKey));
    }

    [Fact]
    public async Task Scan_IsDeduped_SoAChapterIsNeverQueuedTwice()
    {
        var (ctx, manga) = await SeedSeries();
        AddChapter(ctx, manga, "1");
        await ctx.SaveChangesAsync();
        var store = new InMemoryJobStore();

        await DownloadReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);
        await DownloadReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync()); // second tick coalesced on dedup key — no re-queue loop
    }
}
