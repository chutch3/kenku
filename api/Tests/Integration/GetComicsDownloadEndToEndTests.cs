using API.Tests;
using System.Net;
using API;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using API.JobRuntime.Interfaces;
using API.Schema.JobsContext;
using API.Schema.SeriesContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Integration;

/// <summary>
/// The direct-archive download contract through the real runtime: a chapter sourced from the
/// production-registered GetComics connector (Kind = DirectArchive) is fetched as a finished
/// archive and lands on disk as a .cbz, marked Downloaded — no torrent client, no page scraping.
/// Only the network edge is faked (the RateLimitHandler's inner handler serves the archive bytes).
/// </summary>
public class GetComicsDownloadEndToEndTests : IAsyncLifetime
{
    private static readonly byte[] ArchiveBytes = [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    private const string ArchiveUrl = "https://fake.test/battle-beast-9.cbz";
    private const string CoverUrl = "https://fake.test/battle-beast-cover.jpg";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "kenku-gc-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();
        Directory.CreateDirectory(_tempRoot);

        var inner = new FakeHttpMessageHandler(req => req.RequestUri!.ToString() switch
        {
            ArchiveUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ArchiveBytes) },
            CoverUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestImages.Jpeg()) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

        _app = new KenkuApplicationFactory
        {
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            RateLimit = (inner, RequestsPerMinute: 600, QueueLimit: 100, RequestTimeout: TimeSpan.FromSeconds(5)),
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
    public async Task GetComicsChapter_DownloadsTheArchive_AndMarksItDownloaded()
    {
        string libDir = Path.Combine(_tempRoot, "lib");
        Directory.CreateDirectory(libDir);
        var seeded = await _app.WithSeriesContext(async ctx =>
        {
            var library = new FileLibrary(libDir, "Comics");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Invincible Universe – Battle Beast", "", CoverUrl, SeriesReleaseStatus.Continuing, [], [], [], [], library);
            ctx.Series.Add(manga);
            ctx.MangaConnectorToManga.Add(new SourceId<Series>(manga, "GetComics", "Invincible Universe – Battle Beast", null));
            var chapter = new Chapter(manga, "9", null, "Invincible Universe – Battle Beast #9 (2026)");
            ctx.Chapters.Add(chapter);
            var sourceId = new SourceId<Chapter>(chapter, "GetComics", "9", ArchiveUrl, true);
            ctx.MangaConnectorToChapter.Add(sourceId);
            await ctx.SaveChangesAsync();
            return (chapterKey: chapter.Key, sourceKey: sourceId.Key, dir: manga.FullDirectoryPath);
        });

        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobStore>().EnqueueAsync(
                new JobEntity(DownloadChapterHandler.Type, DownloadChapterHandler.PayloadFor(seeded.sourceKey), DateTime.UtcNow));
        await DrainAsync();

        JobEntity downloadJob = await _app.WithJobsContext(c =>
            c.JobQueue.SingleAsync(j => j.Type == DownloadChapterHandler.Type));
        Assert.True(downloadJob.Status == JobStatus.Succeeded,
            $"expected Succeeded, got {downloadJob.Status} (attempts={downloadJob.Attempts}, error={downloadJob.Error})");

        var chapter = await _app.WithSeriesContext(c => c.Chapters.FirstAsync(x => x.Key == seeded.chapterKey));
        Assert.True(chapter.Downloaded, "a fetched archive must mark the chapter Downloaded");
        Assert.NotNull(chapter.FileName);
        string archivePath = Path.Combine(seeded.dir, chapter.FileName!);
        Assert.True(File.Exists(archivePath), $"the archive should exist at {archivePath}");
        Assert.Equal(ArchiveBytes, await File.ReadAllBytesAsync(archivePath));
        Assert.EndsWith(".cbz", archivePath);
    }
}
