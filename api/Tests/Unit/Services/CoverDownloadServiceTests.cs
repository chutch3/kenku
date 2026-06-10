using API.Tests;
using API.HttpRequesters.Interfaces;
using System.Net;
using API;
using API.HttpRequesters;
using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.ActionsContext.Actions;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace API.Tests.Unit.Services;

public class CoverDownloadServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-coversvc-" + Guid.NewGuid().ToString("N"));
    private readonly KenkuSettings _settings;
    private readonly SeriesContext _seriesContext;
    private readonly ActionsContext _actionsContext;

    /// <summary>Minimal connector whose download client is backed by a faked HTTP boundary.</summary>
    public CoverDownloadServiceTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new KenkuSettings { AppData = _root };
        _seriesContext = new SeriesContext(new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _actionsContext = new ActionsContext(new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose()
    {
        _seriesContext.Dispose();
        _actionsContext.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FakeSeriesSource ConnectorServingJpeg()
    {
        var inner = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(TestImages.Jpeg())
        });
        var rateLimit = new RateLimitHandler(_settings, inner);
        return new FakeSeriesSource("Fake:Conn", _settings, httpRequester: new HttpRequester(rateLimit, _settings));
    }

    private async Task<SourceId<Series>> SeedSource(bool useForDownload = true, string coverUrl = "https://example.com/img/cover.png")
    {
        var library = new FileLibrary(Path.Combine(_root, "lib"), "Lib");
        _seriesContext.FileLibraries.Add(library);
        var manga = new Series("Cover Series", "Desc", coverUrl, SeriesReleaseStatus.Continuing,
            [], [], [], [], library);
        _seriesContext.Series.Add(manga);
        var mcId = new SourceId<Series>(manga, "Fake:Conn", "site-id", "https://fake.com/x", useForDownload);
        _seriesContext.MangaConnectorToManga.Add(mcId);
        await _seriesContext.SaveChangesAsync();
        return mcId;
    }

    [Fact]
    public async Task Download_CachesCover_SetsCoverFileNameAndRecordsAction()
    {
        var mcId = await SeedSource();
        var connector = ConnectorServingJpeg();

        await new CoverDownloadService([connector]).DownloadAsync(_seriesContext, _actionsContext, mcId.Key, CancellationToken.None);

        var series = await _seriesContext.Series.FirstAsync();
        Assert.NotNull(series.CoverFileNameInCache);
        Assert.True(File.Exists(Path.Join(_settings.CoverImageCacheOriginal, series.CoverFileNameInCache)));
        Assert.Single(await _actionsContext.Actions.OfType<CoverDownloadedActionRecord>().ToListAsync());
    }

    [Fact]
    public async Task Download_WhenSourceIdNotFound_DoesNothing()
    {
        var connector = ConnectorServingJpeg();

        await new CoverDownloadService([connector]).DownloadAsync(_seriesContext, _actionsContext, "missing-key", CancellationToken.None);

        Assert.Empty(await _actionsContext.Actions.ToListAsync());
    }

    [Fact]
    public async Task Download_WhenCoverUrlEmpty_DoesNothing()
    {
        // Indexer/torrent-sourced series have no cover URL — the job must skip cleanly, not throw an NRE.
        var mcId = await SeedSource(coverUrl: "");
        var connector = ConnectorServingJpeg();

        await new CoverDownloadService([connector]).DownloadAsync(_seriesContext, _actionsContext, mcId.Key, CancellationToken.None);

        Assert.Null((await _seriesContext.Series.FirstAsync()).CoverFileNameInCache);
        Assert.Empty(await _actionsContext.Actions.ToListAsync());
    }

    [Fact]
    public async Task Download_WhenConnectorUnknown_DoesNothing()
    {
        var mcId = await SeedSource();

        await new CoverDownloadService([]).DownloadAsync(_seriesContext, _actionsContext, mcId.Key, CancellationToken.None);

        Assert.Null((await _seriesContext.Series.FirstAsync()).CoverFileNameInCache);
        Assert.Empty(await _actionsContext.Actions.ToListAsync());
    }
}
