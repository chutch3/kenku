using API.HttpRequesters.Interfaces;
using System.Net;
using System.Text;
using API;
using API.Connectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Moq;
using Xunit;

namespace API.Tests.Unit.Connectors;

public class AsuraComicTests
{
    private static KenkuSettings CreateSettings() => new KenkuSettings();

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

    private static Mock<IHeadlessBrowser> CreateMockBrowser(string renderedHtml = "")
    {
        var browser = new Mock<IHeadlessBrowser>();
        browser
            .Setup(b => b.RenderHtmlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(renderedHtml);
        return browser;
    }

    private static SourceId<Series> CreateDummySeries(SeriesSource connector)
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
        var asuracomic = new AsuraComic(settings, CreateMockClient(html).Object, CreateMockBrowser().Object);

        var result = await asuracomic.GetSeriesFromId("some-series-id");

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
        var asuracomic = new AsuraComic(settings, CreateMockClient(html).Object, CreateMockBrowser().Object);

        var seriesId = CreateDummySeries(asuracomic);
        var chapters = asuracomic.GetChapters(seriesId);

        Assert.Single(await chapters);
        // AsuraComic currently does not parse volume, but we want it to. 
        // This test sets up the expectation for the Red/Green/Refactor cycle.
        Assert.Equal(expectedVolume, (await chapters)[0].Item1.VolumeNumber);
        Assert.Equal(expectedChapter, (await chapters)[0].Item1.ChapterNumber);
    }

    [Fact]
    public async Task GetChapterImageUrls_RendersReaderThroughHeadlessBrowser()
    {
        // The reader is JavaScript-rendered, so chapter-image discovery must go through the shared
        // IHeadlessBrowser seam rather than a per-call browser. Given the rendered DOM, the connector
        // parses the chapter-page <img> sources and absolutises relative paths.
        var readerHtml = """
        <html><body>
            <img alt="chapter page 1" src="https://gg.asuracomic.net/chapter/1/01.webp" />
            <img alt="chapter page 2" src="/chapter/1/02.webp" />
        </body></html>
        """;

        var browser = CreateMockBrowser(readerHtml);
        var settings = CreateSettings();
        var asuracomic = new AsuraComic(settings, CreateMockClient("").Object, browser.Object);

        var series = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        var chapter = new Chapter(series, "1", null, null);
        var chapterId = new SourceId<Chapter>(chapter, asuracomic, "chap-1", "https://asuracomic.net/series/x/chapter/1", true);

        var urls = await asuracomic.GetChapterImageUrls(chapterId);

        Assert.Equal(new[]
        {
            "https://gg.asuracomic.net/chapter/1/01.webp",
            "https://asuracomic.net/chapter/1/02.webp",
        }, urls);
        browser.Verify(b => b.RenderHtmlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
