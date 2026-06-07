using API.JobRuntime.Interfaces;
using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.Connectors;
using API.Schema.SeriesContext;
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
public class DownloadChapterJobTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();
    private readonly KenkuApplicationFactory _app;
    private readonly string _libDir = Path.Combine(Path.GetTempPath(), "kenku-dl-" + Guid.NewGuid().ToString("N"));

    private static byte[] Jpeg()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    public DownloadChapterJobTests()
    {
        var settings = new KenkuSettings { AppData = _libDir };
        var connector = new Mock<SeriesSource>("StubConnector", new[] { "en" }, new[] { "stub.test" }, "icon", settings);
        connector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>())).ReturnsAsync(["u1", "u2"]);
        connector.Setup(c => c.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Jpeg()));

        _app = new KenkuApplicationFactory { OutboundHttpTarget = _server.Url!, ExtraConnectors = [connector.Object] };
        Directory.CreateDirectory(_libDir);
    }

    public void Dispose()
    {
        _app.Dispose();
        _server.Stop();
        try { Directory.Delete(_libDir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
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
