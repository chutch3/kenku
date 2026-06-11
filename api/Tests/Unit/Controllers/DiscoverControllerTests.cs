using API.Connectors;
using API.Controllers;
using API.Discovery;
using API.Tests.Unit.JobRuntime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace API.Tests.Unit.Controllers;

public class DiscoverControllerTests
{
    private static readonly DiscoveryEntry Entry = new("Berserk", "c", "u", "AniList", null);

    private static DiscoverController CreateController(KenkuSettings? settings = null) =>
        new(new DiscoveryCache(new FakeClock(DateTime.UtcNow)), settings ?? new KenkuSettings())
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
        aniList.Setup(a => a.GetTrendingMangaAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry]);

        var ok = await CreateController().GetTrendingManga(aniList.Object);

        Assert.Equal("Berserk", Assert.Single(ok.Value!).Title);
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

    [Fact]
    public async Task Feed_SurvivesARateLimitedSubreddit()
    {
        var settings = new KenkuSettings { DiscoveryFeeds = ["limited", "manga"] };
        var reddit = new Mock<IRedditFeedClient>();
        reddit.Setup(r => r.GetHotAsync("limited", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429"));
        reddit.Setup(r => r.GetHotAsync("manga", It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Entry with { Source = "r/manga" }]);

        var ok = await CreateController(settings).GetFeed(reddit.Object);

        Assert.Equal("r/manga", Assert.Single(ok.Value!).Source);
    }
}
