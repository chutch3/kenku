using API.DownloadClients.Interfaces;
using System.IO.Compression;
using API;
using API.Acquirers;
using API.DownloadClients;
using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.Services;

public class TorrentFinalizationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kenku-torfin-" + Guid.NewGuid().ToString("N"));
    private readonly SeriesContext _seriesContext;
    private readonly ActionsContext _actionsContext;

    public TorrentFinalizationServiceTests()
    {
        Directory.CreateDirectory(_root);
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

    private async Task<(Chapter chapter, SourceId<Chapter> chId)> Seed()
    {
        var library = new FileLibrary(Path.Combine(_root, "library"), "Lib");
        _seriesContext.FileLibraries.Add(library);
        var series = new Series("Saga", "d", "c", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _seriesContext.Series.Add(series);
        var chapter = new Chapter(series, "60", null, null);
        _seriesContext.Chapters.Add(chapter);
        var chId = new SourceId<Chapter>(chapter, "FakeTorrent", "60", "magnet:?xt=urn:btih:abc", true);
        _seriesContext.MangaConnectorToChapter.Add(chId);
        await _seriesContext.SaveChangesAsync();
        return (chapter, chId);
    }

    private string SavePathWithCbz()
    {
        string savePath = Path.Combine(_root, "torrent-out");
        Directory.CreateDirectory(savePath);
        using var zip = ZipFile.Open(Path.Combine(savePath, "saga-60.cbz"), ZipArchiveMode.Create);
        zip.CreateEntry("0.jpg");
        return savePath;
    }

    [Fact]
    public async Task Finalize_MovesCbzMarksDownloadedAndRemovesTorrent()
    {
        var (chapter, chId) = await Seed();
        string savePath = SavePathWithCbz();
        var settings = new KenkuSettings { AppData = _root, ChapterNamingScheme = "%M - Ch.%C" };

        var torrent = new Mock<IDownloadClient>();
        torrent.Setup(t => t.Remove(chId.Key, false, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await new TorrentFinalizationService().FinalizeAsync(
            _seriesContext, _actionsContext, torrent.Object, settings, chId.Key, savePath, CancellationToken.None);

        var updated = await _seriesContext.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.True(updated.Downloaded);
        Assert.False(string.IsNullOrEmpty(updated.FileName));
        Assert.True(File.Exists(chapter.GetFullFilepath(settings.ChapterNamingScheme)!));
        Assert.False(File.Exists(Path.Combine(savePath, "saga-60.cbz")), "source .cbz should be moved, not copied");
        torrent.Verify(t => t.Remove(chId.Key, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Finalize_WhenAlreadyDownloaded_IsNoOp()
    {
        var (chapter, chId) = await Seed();
        chapter.Downloaded = true;
        await _seriesContext.SaveChangesAsync();
        string savePath = SavePathWithCbz();
        var settings = new KenkuSettings { AppData = _root, ChapterNamingScheme = "%M - Ch.%C" };

        var torrent = new Mock<IDownloadClient>();

        await new TorrentFinalizationService().FinalizeAsync(
            _seriesContext, _actionsContext, torrent.Object, settings, chId.Key, savePath, CancellationToken.None);

        torrent.Verify(t => t.Remove(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(await _actionsContext.Actions.ToListAsync());
    }
}
