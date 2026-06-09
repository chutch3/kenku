using API.Connectors;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Chapter = API.Schema.SeriesContext.Chapter;
using Series = API.Schema.SeriesContext.Series;
using SourceId = API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>;
using ChapterConnectorId = API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>;

namespace API.Tests.Unit.Services;

public class SeriesChapterSyncServiceTests : IDisposable
{
    private readonly SeriesContext _mangaContext;
    private readonly ActionsContext _actionsContext;

    public SeriesChapterSyncServiceTests()
    {
        var mangaOptions = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mangaContext = new SeriesContext(mangaOptions);

        var actionsOptions = new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _actionsContext = new ActionsContext(actionsOptions);

    }

    public void Dispose()
    {
        _mangaContext.Dispose();
        _actionsContext.Dispose();
    }

    [Fact]
    public async Task DoWork_UpdatesExistingChapterWithMissingVolume()
    {
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        _mangaContext.Series.Add(manga);

        var mockConnector = new Mock<SeriesSource>("MangaDex", new[] { "en" }, new[] { "mangadex.org" }, "icon.png", new KenkuSettings());
        
        var mangaMcId = new SourceId(manga, "MangaDex", "manga-id", "url");
        manga.SourceIds.Add(mangaMcId);
        _mangaContext.MangaConnectorToManga.Add(mangaMcId);

        // Existing chapter with NO volume
        var existingChapter = new Chapter(manga, "1", null, "Title");
        var existingChMcId = new ChapterConnectorId(existingChapter, "MangaDex", "chap-1", "url");
        existingChapter.SourceIds.Add(existingChMcId);
        _mangaContext.Chapters.Add(existingChapter);
        _mangaContext.MangaConnectorToChapter.Add(existingChMcId);
        
        await _mangaContext.SaveChangesAsync();

        // Connector returns the SAME chapter but WITH a volume
        var fetchedChapter = new Chapter(manga, "1", 5, "Title");
        var fetchedChMcId = new ChapterConnectorId(fetchedChapter, "MangaDex", "chap-1", "url");
        fetchedChapter.SourceIds.Add(fetchedChMcId);

        mockConnector.Setup(c => c.GetChapters(It.IsAny<SourceId>(), It.IsAny<string>()))
            .ReturnsAsync([(fetchedChapter, fetchedChMcId)]);
        // Name is set via constructor parameter

        await new SeriesChapterSyncService(new[] { mockConnector.Object })
            .SyncAsync(_mangaContext, _actionsContext, mangaMcId.Key, "en", CancellationToken.None);

        var chapterInDb = await _mangaContext.Chapters.FirstAsync();
        Assert.Equal(5, chapterInDb.VolumeNumber); // This will fail until we fix the worker
    }

    [Fact]
    public async Task Sync_Throws_WhenTheSourceIdDoesNotExist()
    {
        // Swallowing this left the job "Succeeded" while the series sat empty with no signal.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SeriesChapterSyncService([])
                .SyncAsync(_mangaContext, _actionsContext, "no-such-key", "en", CancellationToken.None));
    }

    [Fact]
    public async Task Sync_Throws_WhenTheConnectorIsNotRegistered()
    {
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        _mangaContext.Series.Add(manga);
        var mangaMcId = new SourceId(manga, "GoneConnector", "manga-id", "url");
        manga.SourceIds.Add(mangaMcId);
        _mangaContext.MangaConnectorToManga.Add(mangaMcId);
        await _mangaContext.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SeriesChapterSyncService([])
                .SyncAsync(_mangaContext, _actionsContext, mangaMcId.Key, "en", CancellationToken.None));
        Assert.Contains("GoneConnector", ex.Message);
    }

    [Fact]
    public async Task Sync_RecordsTheRetrievedChapterCount_OnTheActionRecord()
    {
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        _mangaContext.Series.Add(manga);
        var mockConnector = new Mock<SeriesSource>("MangaDex", new[] { "en" }, new[] { "mangadex.org" }, "icon.png", new KenkuSettings());
        var mangaMcId = new SourceId(manga, "MangaDex", "manga-id", "url");
        manga.SourceIds.Add(mangaMcId);
        _mangaContext.MangaConnectorToManga.Add(mangaMcId);
        await _mangaContext.SaveChangesAsync();

        var ch1 = new Chapter(manga, "1", null, null);
        var ch2 = new Chapter(manga, "2", null, null);
        mockConnector.Setup(c => c.GetChapters(It.IsAny<SourceId>(), It.IsAny<string>()))
            .ReturnsAsync([
                (ch1, new ChapterConnectorId(ch1, "MangaDex", "c1", "u1")),
                (ch2, new ChapterConnectorId(ch2, "MangaDex", "c2", "u2")),
            ]);

        await new SeriesChapterSyncService([mockConnector.Object])
            .SyncAsync(_mangaContext, _actionsContext, mangaMcId.Key, "en", CancellationToken.None);

        // The count makes "0 chapters" distinguishable from "never looked" in the activity log.
        var record = Assert.IsType<API.Schema.ActionsContext.Actions.ChaptersRetrievedActionRecord>(
            await _actionsContext.Actions.SingleAsync());
        Assert.Equal(2, record.ChapterCount);
    }
}
