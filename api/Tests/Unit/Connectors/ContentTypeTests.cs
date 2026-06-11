using API.HttpRequesters.Interfaces;
using API;
using API.Connectors;
using API.Controllers;
using API.Indexers.Interfaces;
using Moq;
using Xunit;

namespace API.Tests.Unit.Connectors;

/// <summary>
/// Content type (manga vs comic) is a property of the source, independent of how it acquires
/// files — a page-reader comic site is still a comic source. The UI diverges the comic experience
/// on this, so it must survive into the connector DTO.
/// </summary>
public class ContentTypeTests
{
    private static readonly KenkuSettings Settings = new();

    [Fact]
    public void ComicSources_DeclareComic_MangaScrapersStayManga()
    {
        var http = new Mock<IHttpRequester>().Object;

        Assert.Equal(ContentType.Comic, new GetComics(Settings, http).ContentType);
        Assert.Equal(ContentType.Comic, new ComicHubFree(Settings, http).ContentType);
        Assert.Equal(ContentType.Comic, new IndexerBackedSeriesSource(Mock.Of<IIndexerClient>(), Settings).ContentType);
        Assert.Equal(ContentType.Manga, new WeebCentral(Settings, http).ContentType);
    }

    [Fact]
    public void TheConnectorEndpoint_CarriesTheContentType()
    {
        var http = new Mock<IHttpRequester>().Object;
        var controller = new SeriesSourceController(null!,
            [new ComicHubFree(Settings, http), new WeebCentral(Settings, http)], Settings);

        var result = controller.GetConnectors();

        var comic = Assert.Single(result.Value!, c => c.Name == "ComicHubFree");
        Assert.Equal(ContentType.Comic, comic.ContentType);
        var manga = Assert.Single(result.Value!, c => c.Name == "WeebCentral");
        Assert.Equal(ContentType.Manga, manga.ContentType);
    }
}
