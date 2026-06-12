using API.Connectors;
using API.Schema.SeriesContext;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace API.Tests.Unit.Connectors;

public class GlobalTests
{
    private static Mock<SeriesSource> Connector(KenkuSettings settings, string name, ContentType contentType,
        API.Acquirers.AcquisitionKind kind, string resultName)
    {
        var mock = new Mock<SeriesSource>(name, new[] { "en" }, new[] { name + ".test" }, "icon", settings);
        var manga = new Series(resultName, "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        var id = new SourceId<Series>(manga, mock.Object, name + "-id", "url");
        mock.Setup(c => c.SearchManga(It.IsAny<string>())).ReturnsAsync([(manga, id)]);
        mock.Setup(c => c.ContentType).Returns(contentType);
        mock.Setup(c => c.Kind).Returns(kind);
        mock.Object.Enabled = true;
        return mock;
    }

    [Fact]
    public async Task SearchMangaScoped_FiltersByContentTypeAndSkipsTorrents()
    {
        var settings = new KenkuSettings { DownloadLanguage = "en" };
        var services = new ServiceCollection();
        services.AddSingleton(Connector(settings, "WeebCentral", ContentType.Manga, API.Acquirers.AcquisitionKind.ImageList, "Manga hit").Object);
        services.AddSingleton(Connector(settings, "GetComics", ContentType.Comic, API.Acquirers.AcquisitionKind.DirectArchive, "Comic hit").Object);
        services.AddSingleton(Connector(settings, "Indexers", ContentType.Comic, API.Acquirers.AcquisitionKind.Torrent, "Torrent hit").Object);
        var global = new Global(settings, services.BuildServiceProvider());

        var mangaOnly = await global.SearchMangaScoped("q", ContentType.Manga, includeTorrents: false);
        var comicsNoTorrents = await global.SearchMangaScoped("q", ContentType.Comic, includeTorrents: false);
        var everything = await global.SearchMangaScoped("q", null, includeTorrents: true);

        Assert.Equal("Manga hit", Assert.Single(mangaOnly).Item1.Name);
        Assert.Equal("Comic hit", Assert.Single(comicsNoTorrents).Item1.Name);
        Assert.Equal(3, everything.Length);
    }

    [Fact]
    public async Task SearchManga_SortsByDownloadLanguage()
    {
        var settings = new KenkuSettings { DownloadLanguage = "en" };
        var services = new ServiceCollection();

        var mockItConnector = new Mock<SeriesSource>("Mangaworld", new[] { "it" }, new[] { "mangaworld.mx" }, "icon", settings);
        var mangaIt = new Series("Dan Da Dan IT", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        var idIt = new SourceId<Series>(mangaIt, mockItConnector.Object, "it-id", "url");
        mockItConnector.Setup(c => c.SearchManga(It.IsAny<string>())).ReturnsAsync([(mangaIt, idIt)]);
        mockItConnector.Object.Enabled = true;

        var mockEnConnector = new Mock<SeriesSource>("WeebCentral", new[] { "en" }, new[] { "weebcentral.com" }, "icon", settings);
        var mangaEn = new Series("Dan Da Dan EN", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        var idEn = new SourceId<Series>(mangaEn, mockEnConnector.Object, "en-id", "url");
        mockEnConnector.Setup(c => c.SearchManga(It.IsAny<string>())).ReturnsAsync([(mangaEn, idEn)]);
        mockEnConnector.Object.Enabled = true;

        var mockAllConnector = new Mock<SeriesSource>("Global", new[] { "all" }, new[] { "" }, "icon", settings);

        services.AddSingleton(mockItConnector.Object);
        services.AddSingleton(mockEnConnector.Object);
        services.AddSingleton(mockAllConnector.Object);

        var sp = services.BuildServiceProvider();
        var global = new Global(settings, sp);

        var results = await global.SearchManga("Dan Da Dan");

        Assert.Equal(2, results.Length);
        Assert.Equal("Dan Da Dan EN", results[0].Item1.Name);
        Assert.Equal("Dan Da Dan IT", results[1].Item1.Name);
    }
}
