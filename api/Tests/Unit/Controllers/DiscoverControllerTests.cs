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
        new(new DiscoveryCache(Clock), settings ?? new KenkuSettings())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private sealed class FakeRailSource(KenkuSettings s, List<DiscoveryEntry> entries, ContentType contentType = ContentType.Comic)
        : SeriesSource("FakeComics", ["en"], ["fake.test"], "icon", s), IDiscoveryRailProvider
    {
        public override API.Acquirers.AcquisitionKind Kind => API.Acquirers.AcquisitionKind.DirectArchive;
        public IReadOnlyList<DiscoveryRail> Rails => [new("fake-rail", "Fake", contentType, 1)];
        public Task<List<DiscoveryEntry>> GetRailAsync(string railId, CancellationToken ct) => Task.FromResult(entries);
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)[]> SearchSeries(string m) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetSeriesFromUrl(string url) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetSeriesFromId(string id) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Chapter, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>)[]> GetChapters(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> id) => throw new NotSupportedException();
    }

    // A connector whose DownloadImage is controllable, to exercise the cover proxy without HTTP.
    private sealed class FakeImageSource(KenkuSettings s, byte[]? image)
        : SeriesSource("SeriesFake", ["en"], ["mangafake.test"], "icon", s)
    {
        public override API.Acquirers.AcquisitionKind Kind => API.Acquirers.AcquisitionKind.ImageList;
        public override Task<Stream?> DownloadImage(string imageUrl, CancellationToken ct) =>
            Task.FromResult<Stream?>(image is null ? null : new MemoryStream(image));
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)[]> SearchSeries(string m) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetSeriesFromUrl(string url) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Series, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series>)?> GetSeriesFromId(string id) => throw new NotSupportedException();
        public override Task<(API.Schema.SeriesContext.Chapter, API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter>)[]> GetChapters(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Series> id, string? language = null) => throw new NotSupportedException();
        internal override Task<string[]> GetChapterImageUrls(API.Schema.SeriesContext.SourceId<API.Schema.SeriesContext.Chapter> id) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Cover_StreamsImageFromTheConnector_WhenTheHostIsUnderItsDomain()
    {
        byte[] bytes = [1, 2, 3, 4];
        var connector = new FakeImageSource(new KenkuSettings(), bytes);

        var result = await CreateController().GetCover("SeriesFake", "https://uploads.mangafake.test/c.jpg", [connector]);

        var file = Assert.IsType<FileStreamHttpResult>(result.Result);
        Assert.Equal("image/jpeg", file.ContentType);
        using var ms = new MemoryStream();
        await file.FileStream.CopyToAsync(ms);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task Cover_RedirectsToTheOrigin_ForAnUnknownSource()
    {
        var result = await CreateController().GetCover("Nope", "https://img.example.com/a.jpg", []);

        var redirect = Assert.IsType<RedirectHttpResult>(result.Result);
        Assert.Equal("https://img.example.com/a.jpg", redirect.Url);
    }

    [Fact]
    public async Task Cover_RedirectsToTheOrigin_WhenTheCoverHostIsNotUnderTheConnectorDomain()
    {
        // ComicHubFree serves covers from blogspot.com — not its own host. Don't proxy a foreign host
        // (SSRF); redirect so the browser hotlinks it directly (where hotlinking already works).
        var connector = new FakeImageSource(new KenkuSettings(), [9]);

        var result = await CreateController().GetCover("SeriesFake", "https://3.bp.blogspot.com/x.jpg", [connector]);

        Assert.IsType<RedirectHttpResult>(result.Result);
    }

    [Fact]
    public async Task Cover_RedirectsToTheOrigin_WhenTheConnectorReturnsNoImage()
    {
        var connector = new FakeImageSource(new KenkuSettings(), image: null);

        var result = await CreateController().GetCover("SeriesFake", "https://uploads.mangafake.test/c.jpg", [connector]);

        Assert.IsType<RedirectHttpResult>(result.Result);
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
    public void RailCatalog_ListsEveryDeclaredRail_IncludingDenylistedOnes()
    {
        // The catalog drives the settings toggle UI, so it must show disabled rails too (unlike /Rails).
        var settings = new KenkuSettings { DiscoveryRails = ["fake-rail"] };
        SeriesSource[] connectors = [new FakeRailSource(settings, [])];
        IDiscoveryRailProvider[] standalone = [new AniListRailProvider(new Mock<IAniListClient>().Object, Clock)];

        var ok = CreateController(settings).GetRailCatalog(connectors, standalone);

        Assert.Contains("fake-rail", ok.Value!.Select(r => r.Id)); // denylisted, but still listed
        Assert.Contains("manga-trending", ok.Value!.Select(r => r.Id));
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
