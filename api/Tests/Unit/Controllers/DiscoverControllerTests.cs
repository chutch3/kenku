using API.Connectors;
using API.Controllers;
using API.Discovery;
using API.Schema.DiscoveryContext;
using API.Tests.Unit.JobRuntime;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Unit.Controllers;

public class DiscoverControllerTests
{
    private static readonly DiscoveryEntry Entry = new("Berserk", "c", "u", "AniList", null);
    private static readonly FakeClock Clock = new(new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc));

    private static DiscoverController CreateController(KenkuSettings? settings = null) =>
        new(new DiscoveryCache(Clock), settings ?? new KenkuSettings(), Clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private sealed class FakeLatestSource(KenkuSettings s, List<DiscoveryEntry> latest)
        : SeriesSource("FakeComics", ["en"], ["fake.test"], "icon", s), ILatestSeriesProvider
    {
        public override API.Acquirers.AcquisitionKind Kind => API.Acquirers.AcquisitionKind.DirectArchive;
        public Task<List<DiscoveryEntry>> GetLatestSeriesAsync(CancellationToken ct) => Task.FromResult(latest);
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)[]> SearchManga(string m) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromId(string id) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Chapter, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>)[]> GetChapters(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> id) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Manga_ReturnsTheTrendingRail()
    {
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(AniListShelf.Trending, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);

        var ok = await CreateController().GetTrendingManga(aniList.Object);

        Assert.Equal("Berserk", Assert.Single(ok.Value!).Title);
    }

    [Fact]
    public async Task TopRated_ReturnsTheTopRatedShelf()
    {
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(AniListShelf.TopRated, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);

        var ok = await CreateController().GetTopRatedManga(aniList.Object);

        Assert.Equal("Berserk", Assert.Single(ok.Value!).Title);
    }

    [Fact]
    public async Task New_RequestsPopularMangaStartedInTheClockYear()
    {
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(AniListShelf.NewThisYear(2026), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);

        var ok = await CreateController().GetNewManga(aniList.Object);

        Assert.Equal("Berserk", Assert.Single(ok.Value!).Title);
    }

    [Fact]
    public async Task Genre_ServesAConfiguredGenre_CaseInsensitively()
    {
        var settings = new KenkuSettings { DiscoveryGenres = ["Action"] };
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(AniListShelf.ForGenre("Action"), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);

        var result = await CreateController(settings).GetGenreManga("action", aniList.Object);

        var ok = Assert.IsType<Ok<List<DiscoveryEntry>>>(result.Result);
        Assert.Equal("Berserk", Assert.Single(ok.Value!).Title);
    }

    [Fact]
    public async Task Genre_RejectsAnUnconfiguredGenre()
    {
        var settings = new KenkuSettings { DiscoveryGenres = ["Action"] };

        var result = await CreateController(settings).GetGenreManga("Horror", new Mock<IAniListClient>().Object);

        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task Comics_CollectsFromLatestSeriesProviders_IgnoringOtherConnectors()
    {
        var settings = new KenkuSettings();
        SeriesSource[] connectors =
        [
            new FakeSeriesSource("Plain", settings),
            new FakeLatestSource(settings, [Entry with { Source = "FakeComics" }]),
        ];

        var ok = await CreateController(settings).GetFreshComics(connectors);

        Assert.Equal("FakeComics", Assert.Single(ok.Value!).Source);
    }

    private static DiscoveryContext NewDiscoveryContext() =>
        new(new DbContextOptionsBuilder<DiscoveryContext>()
            .UseInMemoryDatabase("discover-feed-" + Guid.NewGuid().ToString("N")).Options);

    [Fact]
    public async Task Feed_ServesCachedPostsInConfiguredRailOrder()
    {
        var settings = new KenkuSettings { DiscoveryFeeds = ["manga", "comicbooks"] };
        var ctx = NewDiscoveryContext();
        ctx.Posts.AddRange(
            new DiscoveryPost("comicbooks", 0, "Saga thread", "c", "u1", "r/comicbooks", null, Clock.UtcNow),
            new DiscoveryPost("manga", 1, "Second manga thread", "c", "u2", "r/manga", null, Clock.UtcNow),
            new DiscoveryPost("manga", 0, "First manga thread", "c", "u3", "r/manga", null, Clock.UtcNow));
        await ctx.SaveChangesAsync();

        var ok = await CreateController(settings).GetFeed(ctx);

        Assert.Equal(["First manga thread", "Second manga thread", "Saga thread"],
            ok.Value!.Select(e => e.Title));
    }

    [Fact]
    public async Task Feed_IsEmptyBeforeTheFirstSuccessfulRefresh()
    {
        var ok = await CreateController(new KenkuSettings()).GetFeed(NewDiscoveryContext());

        Assert.Empty(ok.Value!);
    }
}
