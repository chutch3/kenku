using API.DownloadClients.Interfaces;
using API.JobRuntime.Reconcilers;
using API;
using API.Acquirers;
using API.DownloadClients;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.MangaConnectors;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The reconciler that replaced the polling half of TorrentCompletionWorker: it enqueues a FinalizeTorrent
/// job for each completed torrent-kind chapter, deduped per source, and enqueues nothing while a torrent
/// is still downloading.
/// </summary>
public class TorrentCompletionReconcilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-tor-rec-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class FakeTorrentSource(KenkuSettings s) : SeriesSource("FakeTorrent", ["en"], ["fake.test"], "i", s)
    {
        public override AcquisitionKind Kind => AcquisitionKind.Torrent;
        public override Task<(Series, SourceId<Series>)[]> SearchManga(string q) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string u) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromId(string i) => throw new NotSupportedException();
        public override Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> m, string? l = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> c) => throw new NotSupportedException();
    }

    private SeriesContext NewContext() =>
        new(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("tor-rec-" + Guid.NewGuid().ToString("N")).Options);

    private async Task<(SeriesContext ctx, SourceId<Chapter> chId)> SeedTorrentChapter()
    {
        var ctx = NewContext();
        var library = new FileLibrary(_root, "Lib");
        ctx.FileLibraries.Add(library);
        var series = new Series("Saga", "d", "c", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        ctx.Series.Add(series);
        var chapter = new Chapter(series, "60", null, null);
        ctx.Chapters.Add(chapter);
        var chId = new SourceId<Chapter>(chapter, "FakeTorrent", "60", "magnet:?xt=urn:btih:abc", true);
        ctx.MangaConnectorToChapter.Add(chId);
        await ctx.SaveChangesAsync();
        return (ctx, chId);
    }

    private FakeTorrentSource[] Connectors => [new FakeTorrentSource(new KenkuSettings { AppData = _root })];

    [Fact]
    public async Task Scan_WhenTorrentCompleted_EnqueuesFinalizeJob()
    {
        var (ctx, chId) = await SeedTorrentChapter();
        var torrent = new Mock<IDownloadClient>();
        torrent.Setup(t => t.GetStatus(chId.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadStatus.Completed(Path.Combine(_root, "out")));
        var store = new InMemoryJobStore();

        await TorrentCompletionReconciler.ScanAndEnqueueAsync(ctx, torrent.Object, Connectors, store, DateTime.UtcNow, default);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(FinalizeTorrentHandler.Type, job.Type);
    }

    [Fact]
    public async Task Scan_WhenTorrentStillDownloading_EnqueuesNothing()
    {
        var (ctx, chId) = await SeedTorrentChapter();
        var torrent = new Mock<IDownloadClient>();
        torrent.Setup(t => t.GetStatus(chId.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadStatus.Downloading(0.3));
        var store = new InMemoryJobStore();

        await TorrentCompletionReconciler.ScanAndEnqueueAsync(ctx, torrent.Object, Connectors, store, DateTime.UtcNow, default);

        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task Scan_IsDedupedPerSource_SoTicksDoNotPileUp()
    {
        var (ctx, chId) = await SeedTorrentChapter();
        var torrent = new Mock<IDownloadClient>();
        torrent.Setup(t => t.GetStatus(chId.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownloadStatus.Completed(Path.Combine(_root, "out")));
        var store = new InMemoryJobStore();

        await TorrentCompletionReconciler.ScanAndEnqueueAsync(ctx, torrent.Object, Connectors, store, DateTime.UtcNow, default);
        await TorrentCompletionReconciler.ScanAndEnqueueAsync(ctx, torrent.Object, Connectors, store, DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync());
    }
}
