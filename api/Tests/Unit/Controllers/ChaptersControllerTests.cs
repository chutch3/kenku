using API.Services.Interfaces;
using API.Acquirers;
using API.Acquirers.Interfaces;
using API.Controllers;
using API.Controllers.Responses;
using API.JobRuntime.Handlers;
using API.Controllers.Requests;
using API.Controllers.DTOs;
using API.Schema.SeriesContext;
using API.JobRuntime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Moq;

namespace API.Tests.Unit.Controllers;

public class ChaptersControllerTests: IDisposable
{
    private readonly string _testWorkDir;

    public ChaptersControllerTests()
    {
        // Ensure the directory exists for KenkuSettings during tests
        _testWorkDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testWorkDir);
    }

    private SeriesContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SeriesContext(options);
    }

    private static ChaptersController CreateController(SeriesContext ctx, API.KenkuSettings? settings = null,
        IEnumerable<API.Connectors.SeriesSource>? connectors = null)
    {
        var testSettings = settings ?? new API.KenkuSettings { AppData = Path.GetTempPath() };

        connectors ??= Enumerable.Empty<API.Connectors.SeriesSource>();
        var mockThumbnailService = new Mock<API.Services.Interfaces.IChapterThumbnailService>();

        var controller = new ChaptersController(ctx, testSettings, connectors, mockThumbnailService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    public void Dispose()
    {
        // Cleanup: Nuke the temp directory and all dummy settings files
        if (Directory.Exists(_testWorkDir))
        {
            Directory.Delete(_testWorkDir, true);
        }
    }

    private static API.Schema.SeriesContext.Series MakeTestManga(string name)
        => new(name, "", "http://example.com/img.jpg", SeriesReleaseStatus.Continuing, [], [], [], []);

    [Fact]
    public async Task MarkAsRequested_StampsTheConfiguredAttemptBudget_OnTheDownloadJob()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Berserk");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "1", null);
        var chId = new API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>(chapter, "Src", "c1", null, false);
        chapter.SourceIds.Add(chId);
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        ctx.MangaConnectorToChapter.Add(chId);
        await ctx.SaveChangesAsync();

        var settings = new API.KenkuSettings { AppData = Path.GetTempPath(), DownloadMaxAttempts = 9 };
        var store = new InMemoryJobStore();
        var connector = new API.Tests.FakeSeriesSource("Src", settings);

        await CreateController(ctx, settings, [connector]).MarkAsRequested(chapter.Key, "Src", true, store, new SystemClock());

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(9, job.MaxAttempts);
    }

    [Fact]
    public async Task UpdateChapter_KnownChapter_UpdatesFileNameAndVolumeNumber()
    {
        // This test now implicitly checks that the absolute path logic inside
        // UpdateChapter doesn't crash when loading settings.
        using var ctx = CreateContext();
        var manga = MakeTestManga("Berserk");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "23", null);
        chapter.FileName = "Berserk - Ch.23.cbz";
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        await ctx.SaveChangesAsync();

        var request = new PatchChapterRecord("Berserk Vol 7/Berserk - Ch.23.cbz", 7);
        var result = await CreateController(ctx).UpdateChapter(chapter.Key, request, new InMemoryJobStore(), new SystemClock());

        Assert.IsType<Ok>(result.Result);
        var updated = await ctx.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal("Berserk Vol 7/Berserk - Ch.23.cbz", updated.FileName);
        Assert.Equal(7, updated.VolumeNumber);
    }

    [Fact]
    public async Task UpdateChapter_UnknownChapterId_ReturnsNotFound()
    {
        using var ctx = CreateContext();

        var request = new PatchChapterRecord("Berserk Vol 1/Berserk - Ch.1.cbz", 1);
        var result = await CreateController(ctx).UpdateChapter("nonexistent-id", request, new InMemoryJobStore(), new SystemClock());

        Assert.IsType<NotFound<string>>(result.Result);
    }

    [Fact]
    public async Task UpdateChapter_NullVolumeNumber_ClearsVolumeNumber()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Berserk");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "1", 5);
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        await ctx.SaveChangesAsync();

        var request = new PatchChapterRecord("Berserk - Ch.1.cbz", null);
        var result = await CreateController(ctx).UpdateChapter(chapter.Key, request, new InMemoryJobStore(), new SystemClock());

        Assert.IsType<Ok>(result.Result);
        var updated = await ctx.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.Equal("Berserk - Ch.1.cbz", updated.FileName);
        Assert.Null(updated.VolumeNumber);
    }


    [Fact]
    public async Task GetChapters_InvalidPagination_ReturnsBadRequest()
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).GetChapters("any-id", filter: null, page: 0, pageSize: 10);
        Assert.IsType<BadRequest>(result.Result);
    }

    [Fact]
    public async Task GetChapters_WithDownloadedFilter_ReturnsOnlyDownloadedChapters()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("One Punch Man");

        var downloadedChapter = new API.Schema.SeriesContext.Chapter(manga, "1", 1) { Downloaded = true };
        var missingChapter = new API.Schema.SeriesContext.Chapter(manga, "2", 1) { Downloaded = false };

        ctx.Series.Add(manga);
        ctx.Chapters.AddRange(downloadedChapter, missingChapter);
        await ctx.SaveChangesAsync();

        var filter = new ChapterFilterRecord(true, null, null, null);
        var response = await CreateController(ctx).GetChapters(manga.Key, filter, page: 1, pageSize: 10);

        var okResult = Assert.IsType<Ok<PagedResponse<API.Controllers.DTOs.Chapter>>>(response.Result);
        var pagedData = okResult.Value;

        Assert.NotNull(pagedData);
        Assert.Single(pagedData.Data);
        Assert.Equal(downloadedChapter.Key, pagedData.Data.First().Key);
    }

    [Fact]
    public async Task GetChapters_MultiplePages_ReturnsCorrectPaginationMetadata()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Naruto");
        ctx.Series.Add(manga);

        for (int i = 1; i <= 15; i++)
        {
            ctx.Chapters.Add(new API.Schema.SeriesContext.Chapter(manga, i.ToString(), null));
        }
        await ctx.SaveChangesAsync();
        var response = await CreateController(ctx).GetChapters(manga.Key, null, page: 1, pageSize: 10);
        var okResult = Assert.IsType<Ok<API.Controllers.DTOs.PagedResponse<API.Controllers.DTOs.Chapter>>>(response.Result);
        var pagedData = okResult.Value;

        Assert.NotNull(pagedData);
        Assert.Equal(2, pagedData.TotalPages);
        Assert.Equal(10, pagedData.Data.Count());
    }

    [Fact]
    public async Task GetChapter_KnownId_ReturnsChapter()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Jujutsu Kaisen");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "1", 1);
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx).GetChapter(chapter.Key);

        var okResult = Assert.IsType<Ok<API.Controllers.DTOs.Chapter>>(result.Result);

        Assert.NotNull(okResult.Value);
        Assert.Equal(chapter.Key, okResult.Value.Key);
    }

    [Fact]
    public async Task GetLatestChapter_UnknownManga_ReturnsNotFound()
    {
        using var ctx = CreateContext();
        var result = await CreateController(ctx).GetLatestChapter("invalid-manga-id");
        Assert.IsType<NotFound<string>>(result.Result);
    }


    [Fact]
    public async Task GetLatestDownloaded_WhenNoneAreDownloaded_ReturnsNoContent()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Mob Psycho 100");

        ctx.Series.Add(manga);
        ctx.Chapters.Add(new API.Schema.SeriesContext.Chapter(manga, "1", 1) { Downloaded = false });
        ctx.Chapters.Add(new API.Schema.SeriesContext.Chapter(manga, "2", 1) { Downloaded = false });
        ctx.Chapters.Add(new API.Schema.SeriesContext.Chapter(manga, "3", 1) { Downloaded = false });
        await ctx.SaveChangesAsync();

        var result = await CreateController(ctx).GetLatestChapterDownloaded(manga.Key);

        Assert.IsType<NoContent>(result.Result);
    }


    [Fact]
    public async Task IgnoreChaptersBefore_ValidManga_UpdatesThresholdInDatabase()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("My Hero Academia");
        ctx.Series.Add(manga);
        await ctx.SaveChangesAsync();

        float newThreshold = 50.5f;
        var result = await CreateController(ctx).IgnoreChaptersBefore(manga.Key, newThreshold);

        Assert.IsType<Ok>(result.Result);
        var updatedManga = await ctx.Series.FirstAsync(m => m.Key == manga.Key);
        Assert.Equal(newThreshold, updatedManga.IgnoreChaptersBefore);
    }

    [Fact]
    public async Task DeleteChapter_ExistingChapter_RemovesFromDatabase()
    {
        using var ctx = CreateContext();
        var manga = MakeTestManga("Attack on Titan");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "1", 1);
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.Chapters.CountAsync());

        var result = await CreateController(ctx).DeleteChapter(chapter.Key);

        Assert.IsType<Ok>(result.Result);
        Assert.Equal(0, await ctx.Chapters.CountAsync());
    }

    [Fact]
    public void PatchChapterRecord_Metadata_IsCompatibleWithModelBinding()
    {
        var modelMetadataProvider = new EmptyModelMetadataProvider();
        Action action = () => modelMetadataProvider.GetMetadataForType(typeof(PatchChapterRecord));

        var exception = Record.Exception(action);
        Assert.Null(exception);
    }

    private sealed class ChoiceResolvingSource(API.KenkuSettings settings, ArchiveResolution resolution)
        : API.Connectors.SeriesSource("FakeArchive", ["en"], ["fake.test"], "icon", settings), IArchiveUrlResolver
    {
        public override AcquisitionKind Kind => AcquisitionKind.DirectArchive;
        public Task<ArchiveResolution> ResolveArchiveUrl(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> chapter, CancellationToken ct) =>
            Task.FromResult(resolution);
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)[]> SearchManga(string m) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromId(string id) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Chapter, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>)[]> GetChapters(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> id) => throw new NotSupportedException();
    }

    private static readonly DownloadOption Empire = new("Spawn #376 (Empire)", "https://getcomics.org/dls/empire", "89 MB");
    private static readonly DownloadOption Series376 = new("Spawn #376", "https://getcomics.org/dls/series", "67 MB");

    private async Task<(SeriesContext ctx, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> chId)> SeedArchiveChapter()
    {
        var ctx = CreateContext();
        var manga = MakeTestManga("Spawn");
        var chapter = new API.Schema.SeriesContext.Chapter(manga, "376", null);
        var chId = new API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>(chapter, "FakeArchive", "376", "https://getcomics.org/c/spawn-376/", true);
        ctx.Series.Add(manga);
        ctx.Chapters.Add(chapter);
        ctx.MangaConnectorToChapter.Add(chId);
        await ctx.SaveChangesAsync();
        return (ctx, chId);
    }

    [Fact]
    public async Task DownloadOptions_ReturnsTheResolvedChoices()
    {
        var (ctx, chId) = await SeedArchiveChapter();
        var source = new ChoiceResolvingSource(new API.KenkuSettings { AppData = _testWorkDir },
            new ArchiveResolution.Choice([Empire, Series376]));

        var result = await CreateController(ctx, connectors: [source]).GetDownloadOptions(chId.Key);

        var ok = Assert.IsType<Ok<DownloadOptionsResponse>>(result.Result);
        Assert.Equal([Empire, Series376], ok.Value!.Options);
        Assert.Null(ok.Value.Reason);
    }

    [Fact]
    public async Task DownloadOptions_ExplainsAPostThatTrulyNeedsManualHandling()
    {
        var (ctx, chId) = await SeedArchiveChapter();
        var source = new ChoiceResolvingSource(new API.KenkuSettings { AppData = _testWorkDir },
            new ArchiveResolution.Manual("only available via MEGA - download manually"));

        var result = await CreateController(ctx, connectors: [source]).GetDownloadOptions(chId.Key);

        var ok = Assert.IsType<Ok<DownloadOptionsResponse>>(result.Result);
        Assert.Empty(ok.Value!.Options);
        Assert.Contains("MEGA", ok.Value.Reason);
    }

    [Fact]
    public async Task Download_EnqueuesAPinnedJob_WhenTheUrlIsStillOffered()
    {
        var (ctx, chId) = await SeedArchiveChapter();
        var source = new ChoiceResolvingSource(new API.KenkuSettings { AppData = _testWorkDir },
            new ArchiveResolution.Choice([Empire, Series376]));
        var store = new InMemoryJobStore();

        var result = await CreateController(ctx, connectors: [source])
            .Download(chId.Key, new PinnedDownloadRequest(Series376.Url), store, new SystemClock());

        Assert.IsType<Ok>(result.Result);
        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(DownloadChapterHandler.Type, job.Type);
        var payload = System.Text.Json.JsonSerializer.Deserialize<DownloadChapterPayload>(job.Payload)!;
        Assert.Equal(chId.Key, payload.ChapterKey);
        Assert.Equal(Series376.Url, payload.PinnedArchiveUrl);
    }

    [Fact]
    public async Task Download_RejectsAUrlThePostNoLongerOffers()
    {
        var (ctx, chId) = await SeedArchiveChapter();
        var source = new ChoiceResolvingSource(new API.KenkuSettings { AppData = _testWorkDir },
            new ArchiveResolution.Choice([Empire]));
        var store = new InMemoryJobStore();

        var result = await CreateController(ctx, connectors: [source])
            .Download(chId.Key, new PinnedDownloadRequest("https://evil.test/whatever"), store, new SystemClock());

        Assert.IsType<BadRequest<string>>(result.Result);
        Assert.Empty(await store.GetAllAsync());
    }
}
