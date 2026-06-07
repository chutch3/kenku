using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The reconciler that replaced UpdateCovers: it enqueues a DownloadCover job per wanted series source,
/// deduped per source so ticks coalesce.
/// </summary>
public class CoverRefreshReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-cover-rec-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext() =>
        new(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("cover-rec-" + Guid.NewGuid().ToString("N")).Options);

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

    [Fact]
    public async Task Scan_EnqueuesACoverJobPerWantedSource()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(manga, "MangaDex", "id1", "url1", true));
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(manga, "WeebCentral", "id2", "url2", true));
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await CoverRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        var jobs = await store.GetAllAsync();
        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j => Assert.Equal(DownloadCoverHandler.Type, j.Type));
    }

    [Fact]
    public async Task Scan_SkipsSourcesNotMarkedForDownload()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(manga, "MangaDex", "id1", "url1", false));
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await CoverRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_IsDedupedPerSource_SoTicksDoNotPileUp()
    {
        var (ctx, manga) = await SeedSeries();
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(manga, "MangaDex", "id1", "url1", true));
        await ctx.SaveChangesAsync();

        var store = new InMemoryJobStore();
        await CoverRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);
        await CoverRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync());
    }
}
