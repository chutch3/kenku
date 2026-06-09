using API.Acquirers.Interfaces;
using API.DownloadClients.Interfaces;
using API.Indexers.Interfaces;
using API;
using API.Acquirers;
using API.Indexers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.Connectors;
using API.DownloadClients;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// The torrent download contract through the real runtime: handing a chapter off to the torrent
/// client is a successful, deferred outcome — not a failure. The torrent is added exactly once
/// (re-runs and reconciler ticks must not re-add while it is in flight), and the chapter is
/// finalised by the completion path once the client reports the torrent done.
/// </summary>
public class TorrentDownloadEndToEndTests : IAsyncLifetime
{
    private sealed class FakeTorrentSource : SeriesSource
    {
        public FakeTorrentSource(KenkuSettings s) : base("FakeTorrent", ["en"], ["fake.test"], "i", s) { }
        public override AcquisitionKind Kind => AcquisitionKind.Torrent;
        public override Task<(Series, SourceId<Series>)[]> SearchManga(string s) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string u) => throw new NotSupportedException();
        public override Task<(Series, SourceId<Series>)?> GetMangaFromId(string i) => throw new NotSupportedException();
        public override Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> m, string? l = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> c) => throw new NotSupportedException();
    }

    /// <summary>In-memory stand-in for qBittorrent: records adds, reports a settable status by tag.</summary>
    private sealed class RecordingDownloadClient : IDownloadClient
    {
        public readonly List<(string Url, string Dir, string Tag)> Added = [];
        public DownloadStatus? Status;

        public Task<string?> Add(string downloadUrl, string saveDir, string tag, CancellationToken ct)
        {
            Added.Add((downloadUrl, saveDir, tag));
            Status ??= new DownloadStatus.Downloading(0);
            return Task.FromResult<string?>(tag);
        }

        public Task<DownloadStatus?> GetStatus(string tag, CancellationToken ct) => Task.FromResult(Status);

        public Task Remove(string tag, bool deleteData, CancellationToken ct)
        {
            Status = null;
            return Task.CompletedTask;
        }
    }

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "kenku-tor-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly PostgresFixture _postgres = new();
    private readonly RecordingDownloadClient _client = new();
    private FakeTorrentSource _source = null!;
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        Directory.CreateDirectory(_tempRoot);

        var settings = new KenkuSettings { AppData = _tempRoot };
        _source = new FakeTorrentSource(settings);

        var release = new IndexerSearchResult("Saga 060.cbz", "magnet:?xt=urn:btih:abc", 1000, 50, "ix");
        var indexer = new Mock<IIndexerClient>();
        indexer.Setup(i => i.Search(It.IsAny<IndexerQuery>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync([release]);

        var acquirer = new TorrentAcquirer(indexer.Object, _client,
            new ReleaseSelector { MinSeeders = 1 },
            new TorrentAcquirerSettings(Path.Combine(_tempRoot, "staging"), [8000]));

        _app = new KenkuApplicationFactory
        {
            ExtraConnectors = [_source],
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            ExtraServices = services =>
            {
                services.AddSingleton<IDownloadClient>(_client);
                services.AddSingleton<IChapterAcquirer>(acquirer);
            },
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        await _postgres.DropDatabaseAsync(_dbName);
    }

    private async Task DrainAsync()
    {
        using var scope = _app.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<Dispatcher>();
        while (await dispatcher.RunOnceAsync()) { }
    }

    [Fact]
    public async Task TorrentChapter_HandsOffOnce_DefersWithoutFailing_AndFinalizesOnCompletion()
    {
        string libDir = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(libDir);
        var seeded = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Saga", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "60", null, null);
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "FakeTorrent", "60", null, true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (chapterKey: chapter.Key, sourceKey: sourceId.Key, dir: manga.FullDirectoryPath);
        });

        // Hand-off: the download job adds the torrent and succeeds as "deferred" — not Failed.
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(DownloadChapterHandler.Type, DownloadChapterHandler.PayloadFor(seeded.sourceKey), DateTime.UtcNow));
        await DrainAsync();

        Assert.Equal(1, _client.Added.Count);
        JobEntity downloadJob = await _app.WithJobsContext(c =>
            c.JobQueue.SingleAsync(j => j.Type == DownloadChapterHandler.Type));
        Assert.Equal(JobStatus.Succeeded, downloadJob.Status);

        var chapterMid = await _app.WithSeriesContext(c => c.Chapters.FirstAsync(x => x.Key == seeded.chapterKey));
        Assert.False(chapterMid.Downloaded, "hand-off must not mark the chapter Downloaded");

        // In flight: a reconciler tick must not enqueue another download (and so never re-add the torrent).
        await _app.WithSeriesContext(async ctx =>
        {
            using var scope = _app.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            return await DownloadReconciler.ScanAndEnqueueAsync(ctx, store, DateTime.UtcNow,
                _client, [_source], CancellationToken.None);
        });
        await DrainAsync();
        Assert.Equal(1, _client.Added.Count);

        // Completion: the client reports done; the completion reconciler finalises the chapter.
        string saveDir = Path.Combine(_tempRoot, "staging", seeded.sourceKey);
        Directory.CreateDirectory(saveDir);
        await File.WriteAllBytesAsync(Path.Combine(saveDir, "Saga 060.cbz"), [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        _client.Status = new DownloadStatus.Completed(saveDir);

        await _app.WithSeriesContext(async ctx =>
        {
            using var scope = _app.Services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
            return await TorrentCompletionReconciler.ScanAndEnqueueAsync(ctx, _client, [_source], store,
                DateTime.UtcNow, CancellationToken.None);
        });
        await DrainAsync();

        var chapter = await _app.WithSeriesContext(c => c.Chapters.FirstAsync(x => x.Key == seeded.chapterKey));
        Assert.True(chapter.Downloaded, "a completed torrent should finalise the chapter");
        Assert.NotNull(chapter.FileName);
    }
}
