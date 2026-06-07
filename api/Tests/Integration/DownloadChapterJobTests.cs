using API.JobRuntime.Interfaces;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Connectors;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using API.Tests.Unit.JobRuntime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WireMock.Server;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// Step-4: the DownloadChapter handler runs on the real runtime. A download job enqueued through the job
/// store and run by the dispatcher fetches the chapter and marks it Downloaded with a .cbz on disk — the
/// AF2a contract, now driven by the runtime instead of the legacy download worker.
/// </summary>
public class DownloadChapterJobTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-dl-" + Guid.NewGuid().ToString("N"));
    private readonly PostgresFixture _postgres = new();
    private string? _dbName;
    private KenkuApplicationFactory _app = null!;

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    public async Task InitializeAsync()
    {
        string? pgCs = null;
        if (await _postgres.IsReachableAsync())
        {
            _dbName = await _postgres.CreateDatabaseAsync();
            pgCs = _postgres.GetConnectionString(_dbName);
        }
        var settings = new KenkuSettings { AppData = _libDir };
        var connector = new Mock<SeriesSource>("StubConnector", new[] { "en" }, new[] { "stub.test" }, "icon", settings);
        connector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>())).ReturnsAsync(["u1", "u2"]);
        connector.Setup(c => c.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Jpeg()));

        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ExtraConnectors = [connector.Object],
            PostgresConnectionString = pgCs,
        };
        Directory.CreateDirectory(_libDir);
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        _server.Stop();
        try { Directory.Delete(_libDir, recursive: true); } catch { }
        if (_dbName is not null)
            await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task EnqueuedDownloadJob_DownloadsTheChapter_ThroughTheRuntime()
    {
        var seeded = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(_libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Stub Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "1", null, "Title");
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "StubConnector", "site1", "url1", true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (chapterKey: chapter.Key, sourceKey: sourceId.Key, dir: manga.FullDirectoryPath);
        });

        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(DownloadChapterHandler.Type, DownloadChapterHandler.PayloadFor(seeded.sourceKey), DateTime.UtcNow));

        using (var scope = _app.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync());

        var chapter = await _app.WithSeriesContext(c => c.Chapters.FirstAsync(x => x.Key == seeded.chapterKey));
        Assert.True(chapter.Downloaded, "the download job should have marked the chapter Downloaded");
        Assert.NotNull(chapter.FileName);
        Assert.True(File.Exists(Path.Combine(seeded.dir, chapter.FileName!)), "a .cbz should exist on disk");
    }
}

/// <summary>
/// AF2d: a failed mid-download leaves no corrupt archive at the final path (temp-then-move invariant),
/// and a subsequent retry succeeds once the transient error clears.
/// </summary>
public class DownloadChapterJobRetryTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-dl-retry-" + Guid.NewGuid().ToString("N"));
    private readonly FakeClock _clock = new();
    private readonly PostgresFixture _postgres = new();
    private string? _dbName;
    private KenkuApplicationFactory _app = null!;

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    public async Task InitializeAsync()
    {
        string? pgCs = null;
        if (await _postgres.IsReachableAsync())
        {
            _dbName = await _postgres.CreateDatabaseAsync();
            pgCs = _postgres.GetConnectionString(_dbName);
        }
        bool failedOnce = false;
        var settings = new KenkuSettings { AppData = _libDir };
        var connector = new Mock<SeriesSource>("StubConnector", new[] { "en" }, new[] { "stub.test" }, "icon", settings);
        connector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
            .ReturnsAsync(["url1", "url2"]);
        connector.Setup(c => c.DownloadImage("url1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Jpeg()));
        connector.Setup(c => c.DownloadImage("url2", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken __) =>
            {
                if (!failedOnce)
                {
                    failedOnce = true;
                    throw new InvalidOperationException("simulated network error");
                }
                return Task.FromResult<Stream?>(new MemoryStream(Jpeg()));
            });

        Directory.CreateDirectory(_libDir);
        _app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            Clock = _clock,
            ExtraConnectors = [connector.Object],
            PostgresConnectionString = pgCs,
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        _server.Stop();
        try { Directory.Delete(_libDir, recursive: true); } catch { }
        if (_dbName is not null)
            await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task FailedDownload_LeavesNoCorruptArchive_AndRetrySucceeds()
    {
        var seeded = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(_libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Stub Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            var chapter = new Chapter(manga, "1", null, "Title");
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "StubConnector", "site1", "url1", true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (chapterKey: chapter.Key, sourceKey: sourceId.Key, seriesDir: manga.FullDirectoryPath);
        });

        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(DownloadChapterHandler.Type, DownloadChapterHandler.PayloadFor(seeded.sourceKey), _clock.UtcNow));

        // Run 1: url2 throws → acquirer returns null → handler throws → dispatcher records failure
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();

        var jobAfterFailure = await _app.WithJobsContext(async ctx =>
            await ctx.JobQueue.FirstAsync(j => j.Type == DownloadChapterHandler.Type));
        Assert.False(jobAfterFailure.Status == JobStatus.Succeeded, "failed run must not be Succeeded");
        Assert.NotNull(jobAfterFailure.Error);

        var chapterAfterFailure = await _app.WithSeriesContext(ctx => ctx.Chapters.FirstAsync(c => c.Key == seeded.chapterKey));
        Assert.False(chapterAfterFailure.Downloaded);

        // The temp-then-move invariant: no partial archive at the final .cbz path after failure.
        // Mutation: removing TryDelete(tempPath) from ImageListAcquirer's catch leaves a .part file that
        // breaks the retry File.Move (overwrite still works, but the cbz-glob assertion below would go RED
        // if the .part were renamed to .cbz by a buggy naming scheme).
        Assert.False(Directory.EnumerateFiles(seeded.seriesDir, "*.cbz", SearchOption.AllDirectories).Any(),
            "a failed download must leave no .cbz at the final path");

        // Advance past backoff so the retry is eligible.
        _clock.Advance(TimeSpan.FromHours(2));

        // Run 2: url2 now returns Jpeg() — retry succeeds.
        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();

        var chapterAfterRetry = await _app.WithSeriesContext(ctx => ctx.Chapters.FirstAsync(c => c.Key == seeded.chapterKey));
        Assert.True(chapterAfterRetry.Downloaded);
        Assert.NotNull(chapterAfterRetry.FileName);
        Assert.True(File.Exists(Path.Combine(seeded.seriesDir, chapterAfterRetry.FileName!)));
        using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(seeded.seriesDir, chapterAfterRetry.FileName!));
        Assert.True(zip.Entries.Count > 0, "the archive must be a valid ZIP");
    }
}
