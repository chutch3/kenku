using API.Connectors;
using API.Discovery;
using API.Tests.Unit.JobRuntime;
using Moq;
using Xunit;

namespace API.Tests.Unit.Discovery;

public class AniListRailProviderTests
{
    private static readonly FakeClock Clock = new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void DeclaresTheThreeMangaShelfRails_InOrder()
    {
        var provider = new AniListRailProvider(new Mock<IAniListClient>().Object, Clock);

        Assert.Equal(["manga-trending", "manga-new", "manga-top-rated"], provider.Rails.Select(r => r.Id));
        Assert.All(provider.Rails, r => Assert.Equal(ContentType.Manga, r.ContentType));
        // Orders are ascending so the aggregator can interleave other providers' rails deterministically.
        Assert.True(provider.Rails.Select(r => r.Order).SequenceEqual(provider.Rails.Select(r => r.Order).Order()));
    }

    [Fact]
    public async Task GetRailAsync_Trending_HitsTheTrendingShelf()
    {
        var aniList = new Mock<IAniListClient>();
        aniList.Setup(a => a.GetMangaListAsync(AniListShelf.Trending, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DiscoveryEntry("Berserk", "c", "u", "AniList", null)]);

        var entries = await new AniListRailProvider(aniList.Object, Clock).GetRailAsync("manga-trending", default);

        Assert.Equal("Berserk", Assert.Single(entries).Title);
    }
}
