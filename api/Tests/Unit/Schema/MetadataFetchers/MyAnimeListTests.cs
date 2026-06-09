using API;
using API.Schema.SeriesContext;
using API.Schema.SeriesContext.MetadataFetchers;
using JikanDotNet;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Unit.Schema.MetadataFetchers;

public class MyAnimeListTests
{
    private static async Task<(SeriesContext context, Series series)> Seed(string coverUrl)
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase("mal-update-" + Guid.NewGuid().ToString("N")).Options;
        var context = new SeriesContext(options);
        var library = new FileLibrary("/tmp", "Lib");
        context.FileLibraries.Add(library);
        var series = new Series("Berserk", "", coverUrl, SeriesReleaseStatus.Continuing,
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, null, "en");
        context.Series.Add(series);
        await context.SaveChangesAsync();
        // The InMemory provider cannot re-load owned collections (AltTitles) without their owner;
        // mark them loaded so UpdateMetadata's collection loading no-ops, as it would after Include.
        foreach (var collection in context.Entry(series).Collections)
            collection.IsLoaded = true;
        return (context, series);
    }

    private static Mock<IJikan> JikanReturning(string? imageUrl)
    {
        var response = new BaseJikanResponse<MangaFull>
        {
            Data = new MangaFull
            {
                Titles = [new TitleEntry { Type = "Default", Title = "Berserk" }],
                Synopsis = "Struggler.",
                Authors = [],
                Images = new ImagesSet { JPG = new Image { ImageUrl = imageUrl } },
            },
        };
        var jikan = new Mock<IJikan>();
        jikan.Setup(j => j.GetMangaFullDataAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        return jikan;
    }

    [Fact]
    public async Task UpdateMetadata_BackfillsTheCoverUrl_WhenTheSeriesHasNone()
    {
        // The Chainsaw Man / Berserk case: the connector page yielded no cover, but the linked
        // MyAnimeList entry has one — metadata refresh must backfill it so the cover path can fetch it.
        var (context, series) = await Seed(coverUrl: "");
        var mal = new MyAnimeList(JikanReturning("https://cdn.myanimelist.net/images/manga/1/157897.jpg").Object);
        var entry = new MetadataEntry(mal, series, "2");

        await mal.UpdateMetadata(entry, context, CancellationToken.None);

        Assert.Equal("https://cdn.myanimelist.net/images/manga/1/157897.jpg", series.CoverUrl);
    }

    [Fact]
    public async Task UpdateMetadata_NeverOverridesAConnectorCover()
    {
        var (context, series) = await Seed(coverUrl: "https://weebcentral.com/covers/berserk.png");
        var mal = new MyAnimeList(JikanReturning("https://cdn.myanimelist.net/other.jpg").Object);
        var entry = new MetadataEntry(mal, series, "2");

        await mal.UpdateMetadata(entry, context, CancellationToken.None);

        Assert.Equal("https://weebcentral.com/covers/berserk.png", series.CoverUrl);
    }
}
