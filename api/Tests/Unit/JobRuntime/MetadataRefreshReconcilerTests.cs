using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The reconciler that replaced the bulk UpdateMetadataWorker: it enqueues a RefreshExternalMetadata job
/// per tracked series that has a metadata entry, deduped.
/// </summary>
public class MetadataRefreshReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-mrr-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SeriesContext NewContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("mrr-" + Guid.NewGuid().ToString("N")).Options;
        return new SeriesContext(options);
    }

    private async Task<SeriesContext> Seed()
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var tracked = new Series("Tracked", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        var untracked = new Series("Untracked", "", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        ctx.Series.AddRange(tracked, untracked);
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(tracked, "MockConnector", "t1", "url", useForDownload: true));
        ctx.MangaConnectorToManga.Add(new SourceId<Series>(untracked, "MockConnector", "u1", "url", useForDownload: false));
        var fetcher = new Mock<MetadataFetcher>("MyAnimeList").Object;
        ctx.Set<MetadataEntry>().Add(new MetadataEntry(fetcher, tracked, "mal-1"));
        ctx.Set<MetadataEntry>().Add(new MetadataEntry(fetcher, untracked, "mal-2"));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Fact]
    public async Task Scan_EnqueuesARefreshJob_OnlyForTrackedSeriesWithMetadata()
    {
        using var ctx = await Seed();
        var store = new InMemoryJobStore();

        int enqueued = await MetadataRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Equal(1, enqueued);
        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(RefreshExternalMetadataHandler.Type, job.Type);
    }

    [Fact]
    public async Task Scan_IsDeduped_SoTicksDoNotPileUp()
    {
        using var ctx = await Seed();
        var store = new InMemoryJobStore();

        await MetadataRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);
        await MetadataRefreshReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync());
    }
}
