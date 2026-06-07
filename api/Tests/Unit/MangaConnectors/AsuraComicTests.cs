using API.HttpRequesters.Interfaces;
using System.Net;
using System.Text;
using API;
using API.MangaConnectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Moq;
using Xunit;

namespace API.Tests.Unit.MangaConnectors;

public class AsuraComicTests
{
    private static KenkuSettings CreateSettings() => new KenkuSettings();
    private static RateLimitHandler CreateRateLimitHandler() => new RateLimitHandler(CreateSettings());

    private static Mock<IHttpRequester> CreateMockClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var mockClient = new Mock<IHttpRequester>();
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseContent, Encoding.UTF8, "text/html")
        };
        mockClient
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(response);
        return mockClient;
    }

    private static SourceId<Series> CreateDummyManga(SeriesSource connector)
    {
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        return new SourceId<Series>(manga, connector, "test-id", "https://example.com/test");
    }

    [Fact]
    public async Task GetMangaFromId_CapturesExternalTrackerLinks()
    {
        // When a series page links out to AniList/MangaUpdates, the connector keeps those ids so the
        // series can be matched to a metadata source by identifier rather than a fuzzy title guess.
        var html = """
        <html><head><title>Fire Punch - Asura Scans</title></head>
        <body>
            <a href="https://anilist.co/manga/87170">AniList</a>
            <a href="https://www.mangaupdates.com/series/eur1ktv">MangaUpdates</a>
            <a href="https://asuracomic.net/series/abc">internal nav</a>
        </body></html>
        """;

        var settings = CreateSettings();
        var asuracomic = new AsuraComic(settings, CreateRateLimitHandler(), CreateMockClient(html).Object);

        var result = await asuracomic.GetMangaFromId("some-series-id");

        Assert.NotNull(result);
        var links = result.Value.Item1.Links;
        Assert.Contains(links, l => l.LinkProvider == "AniList" && l.LinkUrl == "https://anilist.co/manga/87170");
        Assert.DoesNotContain(links, l => l.LinkUrl.Contains("asuracomic.net"));
    }

    [Theory]
    [InlineData("Chapter 1", null, "1")]
    [InlineData("Vol. 2 Chapter 3", 2, "3")]
    [InlineData("Season 1 Chapter 4", 1, "4")]
    public async Task GetChapters_ParsesVolumeFromText(string linkText, int? expectedVolume, string expectedChapter)
    {
        var html = $$"""
        <html>
        <body>
            <a href="/chapter/{{expectedChapter}}">
                {{linkText}}
            </a>
        </body>
        </html>
        """;

        var settings = CreateSettings();
        var asuracomic = new AsuraComic(settings, CreateRateLimitHandler(), CreateMockClient(html).Object);

        var mangaId = CreateDummyManga(asuracomic);
        var chapters = asuracomic.GetChapters(mangaId);

        Assert.Single(await chapters);
        // AsuraComic currently does not parse volume, but we want it to. 
        // This test sets up the expectation for the Red/Green/Refactor cycle.
        Assert.Equal(expectedVolume, (await chapters)[0].Item1.VolumeNumber);
        Assert.Equal(expectedChapter, (await chapters)[0].Item1.ChapterNumber);
    }
}
