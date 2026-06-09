using API.JobRuntime.Interfaces;
using System.Net;
using API.Connectors;
using API.HttpRequesters;
using API.HttpRequesters.Interfaces;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using API.Tests.Unit.JobRuntime;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WireMock.Server;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// AF1a: SyncSeriesChapters job persists chapters through the real runtime.
/// AF1b: DownloadCover job sets CoverFileNameInCache through the real runtime.
/// </summary>
public class ConnectorFlowEndToEndTests : IAsyncLifetime
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-cf-" + Guid.NewGuid().ToString("N"));
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _server.Stop();
        try { Directory.Delete(_libDir, recursive: true); } catch { }
        await _postgres.DropDatabaseAsync(_dbName);
    }

    [Fact]
    public async Task SyncChaptersJob_PersistsChapters_ThroughRuntime()
    {
        var settings = new KenkuSettings { AppData = _libDir };
        var connector = new Mock<SeriesSource>("ChapterSrc", new[] { "en" }, new[] { "chaptersrc.test" }, "icon", settings);
        connector.Setup(c => c.GetChapters(It.IsAny<SourceId<Series>>(), It.IsAny<string?>()))
            .Returns((SourceId<Series> srcId, string? lang) =>
            {
                var ch1 = new Chapter(srcId.Obj, "1", null, null);
                var ch2 = new Chapter(srcId.Obj, "2", null, null);
                return Task.FromResult(new (Chapter, SourceId<Chapter>)[]
                {
                    (ch1, new SourceId<Chapter>(ch1, "ChapterSrc", "ch-1", "http://chaptersrc.test/c/1")),
                    (ch2, new SourceId<Chapter>(ch2, "ChapterSrc", "ch-2", "http://chaptersrc.test/c/2")),
                });
            });

        using var app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ExtraConnectors = [connector.Object],
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
        };
        Directory.CreateDirectory(_libDir);

        string seriesKey = "";
        string sourceIdKey = await app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(_libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test Series", "", "http://x/c.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            seriesKey = manga.Key;
            var sourceId = new SourceId<Series>(manga, "ChapterSrc", "site-id-1", "http://chaptersrc.test/s/1", true);
            ctx.MangaConnectorToManga.Add(sourceId);
            await ctx.SaveChangesAsync();
            return sourceId.Key;
        });

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(SyncSeriesChaptersHandler.Type, SyncSeriesChaptersHandler.PayloadFor(sourceIdKey, "en"), DateTime.UtcNow));

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();

        int count = await app.WithSeriesContext(ctx =>
            ctx.Chapters.CountAsync(c => c.ParentManga.Key == seriesKey));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SyncChaptersJob_WhoseFetchFails_EndsNeedsAttention_NotSilentlySucceeded()
    {
        // The "I am a hero" incident: the chapter-list fetch failed, the sync swallowed it, and the job
        // reported Succeeded with 0 chapters — invisible. A failed fetch must end NeedsAttention with
        // the reason, after bounded retries.
        var settings = new KenkuSettings { AppData = _libDir };
        var connector = new Mock<SeriesSource>("ChapterSrc", new[] { "en" }, new[] { "chaptersrc.test" }, "icon", settings);
        connector.Setup(c => c.GetChapters(It.IsAny<SourceId<Series>>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("chapter list request failed: HTTP 404"));

        var clock = new FakeClock();
        using var app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ExtraConnectors = [connector.Object],
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            Clock = clock,
        };
        Directory.CreateDirectory(_libDir);

        string sourceIdKey = await app.WithSeriesContext(async ctx =>
        {
            var manga = new Series("I Am A Hero", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
            ctx.Series.Add(manga);
            var sourceId = new SourceId<Series>(manga, "ChapterSrc", "01ABC/I-Am-A-Hero", "http://chaptersrc.test/s/1", true);
            ctx.MangaConnectorToManga.Add(sourceId);
            await ctx.SaveChangesAsync();
            return sourceId.Key;
        });

        JobEntity job;
        using (var scope = app.Services.CreateScope())
            job = await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(SyncSeriesChaptersHandler.Type, SyncSeriesChaptersHandler.PayloadFor(sourceIdKey, "en"), clock.UtcNow));

        for (int i = 0; i < 20; i++)
        {
            using var scope = app.Services.CreateScope();
            if (!await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync()) break;
            clock.Advance(TimeSpan.FromHours(1)); // past any backoff window
        }

        JobEntity finished = await app.WithJobsContext(c => c.JobQueue.SingleAsync(j => j.Key == job.Key));
        Assert.Equal(API.Schema.JobsContext.JobStatus.NeedsAttention, finished.Status);
        Assert.Contains("404", finished.Error);
    }

    [Fact]
    public async Task DownloadCoverJob_SetsCoverFileNameInCache_WithOwnScope()
    {
        var jpegBytes = Jpeg();
        var httpRequester = new Mock<IHttpRequester>();
        httpRequester
            .Setup(r => r.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string?>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jpegBytes),
            });

        using var app = new KenkuApplicationFactory
        {
            OutboundHttpTarget = _server.Url!,
            ConnectorHttpRequester = httpRequester.Object,
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
        };
        Directory.CreateDirectory(_libDir);

        string sourceIdKey = await app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(_libDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("WeebCentral Cover Series", "", "http://weebcentral.com/img/cover.jpg", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            var sourceId = new SourceId<Series>(manga, "WeebCentral", "site-wc-1", "http://weebcentral.com/s/1", true);
            ctx.MangaConnectorToManga.Add(sourceId);
            await ctx.SaveChangesAsync();
            return sourceId.Key;
        });

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(DownloadCoverHandler.Type, DownloadCoverHandler.PayloadFor(sourceIdKey), DateTime.UtcNow));

        using (var scope = app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Dispatcher>().RunOnceAsync();

        var series = await app.WithSeriesContext(ctx => ctx.Series.FirstAsync());
        Assert.NotNull(series.CoverFileNameInCache);

        var kenkuSettings = app.Services.GetRequiredService<KenkuSettings>();
        Assert.True(File.Exists(Path.Combine(kenkuSettings.CoverImageCacheOriginal, series.CoverFileNameInCache!)),
            "cover file must exist in the cache");
    }
}
