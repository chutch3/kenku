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

    private sealed class FakeRailSource(KenkuSettings s, List<DiscoveryEntry> entries, ContentType contentType = ContentType.Comic)
        : SeriesSource("FakeComics", ["en"], ["fake.test"], "icon", s), IDiscoveryRailProvider
    {
        public override API.Acquirers.AcquisitionKind Kind => API.Acquirers.AcquisitionKind.DirectArchive;
        public IReadOnlyList<DiscoveryRail> Rails => [new("fake-rail", "Fake", contentType, 1)];
        public Task<List<DiscoveryEntry>> GetRailAsync(string railId, CancellationToken ct) => Task.FromResult(entries);
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)[]> SearchManga(string m) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromUrl(string url) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetMangaFromId(string id) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Chapter, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>)[]> GetChapters(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> id) => throw new NotSupportedException();
    }

    [Fact]
    public void Genres_ReturnsAniListSupportedGenres()
    {
        var ok = CreateController().GetGenres();

        Assert.Contains("Action", ok.Value!);
        Assert.Contains("Slice of Life", ok.Value!);
        // "Gore" is not an AniList genre — it must not be offered.
        Assert.DoesNotContain("Gore", ok.Value!);
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
    public async Task Comics_CollectsComicRailProviders_IgnoringOtherConnectors()
    {
        var settings = new KenkuSettings();
        SeriesSource[] connectors =
        [
            new FakeSeriesSource("Plain", settings),
            new FakeRailSource(settings, [Entry with { Source = "FakeComics" }]),
        ];

        var ok = await CreateController(settings).GetFreshComics(connectors);

        Assert.Equal("FakeComics", Assert.Single(ok.Value!).Source);
    }

    [Fact]
    public async Task Rails_AggregatesProviders_OrdersByRailOrder_AndAppliesTheDenylist()
    {
        var settings = new KenkuSettings { DiscoveryRails = ["fake-rail"] }; // disable the comic fake rail
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(It.IsAny<AniListShelf>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);
        SeriesSource[] connectors = [new FakeRailSource(settings, [Entry with { Source = "FakeComics" }])];
        IDiscoveryRailProvider[] standalone = [new AniListRailProvider(aniList.Object, Clock)];

        var ok = await CreateController(settings).GetRails(connectors, standalone);

        var rails = ok.Value!;
        // fake-rail (order 1) is denylisted → gone; the 3 AniList rails (10/40/50) remain, in order.
        Assert.Equal(["manga-trending", "manga-new", "manga-top-rated"], rails.Select(r => r.Id));
        Assert.All(rails, r => Assert.Equal(ContentType.Manga, r.ContentType));
        Assert.Equal("Berserk", Assert.Single(rails[0].Entries).Title);
    }

    [Fact]
    public async Task Rails_IncludesEnabledRailsFromEveryProvider_InGlobalOrder()
    {
        var settings = new KenkuSettings();
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(It.IsAny<AniListShelf>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);
        SeriesSource[] connectors = [new FakeRailSource(settings, [Entry with { Source = "FakeComics" }])]; // fake-rail order 1
        IDiscoveryRailProvider[] standalone = [new AniListRailProvider(aniList.Object, Clock)];

        var ok = await CreateController(settings).GetRails(connectors, standalone);

        // Global order is by Rail.Order: fake-rail (1) before the AniList shelves (10/40/50).
        Assert.Equal(["fake-rail", "manga-trending", "manga-new", "manga-top-rated"], ok.Value!.Select(r => r.Id));
    }

    [Fact]
    public async Task Comics_ServesOnlyComicRails_NotMangaOnesFromTheSameSeam()
    {
        var settings = new KenkuSettings();
        SeriesSource[] connectors =
        [
            new FakeRailSource(settings, [Entry with { Source = "MangaRail" }], ContentType.Manga),
        ];

        var ok = await CreateController(settings).GetFreshComics(connectors);

        // A provider declaring a Manga rail must not leak into the comics endpoint.
        Assert.Empty(ok.Value!);
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
