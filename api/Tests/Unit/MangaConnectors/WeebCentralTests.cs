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

public class WeebCentralTests
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

    [Theory]
    [InlineData("Volume 1 Chapter 2", 1, "2")]
    [InlineData("Vol 3 Chapter 4", 3, "4")]
    [InlineData("Season 5 Episode 6", 5, "6")]
    [InlineData("Chapter 10", null, "10")] // No volume
    public async Task GetChapters_ParsesVolumeFromText(string linkText, int? expectedVolume, string expectedChapter)
    {
        var html = $$"""
        <html>
        <body>
            <a href="/chapters/chap-1">
                <span class="">{{linkText}}</span>
            </a>
        </body>
        </html>
        """;

        var settings = CreateSettings();
        var weebCentral = new WeebCentral(settings, CreateMockClient(html).Object);

        var mangaId = CreateDummyManga(weebCentral);
        var chapters = await weebCentral.GetChapters(mangaId);

        Assert.Single(chapters);
        Assert.Equal(expectedVolume, chapters[0].Item1.VolumeNumber);
        Assert.Equal(expectedChapter, chapters[0].Item1.ChapterNumber);
    }

    [Fact]
    public async Task GetMangaFromId_CapturesExternalTrackerLinks()
    {
        // WeebCentral series pages link out to AniList / MangaUpdates. Those external ids are the key
        // to matching the series to a MangaDex entry by identifier instead of a fuzzy title guess, so
        // the connector must keep them instead of discarding the link list.
        var html = """
        <html><head><title>Fire Punch | Weeb Central</title></head>
        <body>
            <section>
                <strong>Associated Names</strong>
                <a href="https://anilist.co/manga/87170">AniList</a>
                <a href="https://www.mangaupdates.com/series/eur1ktv">MangaUpdates</a>
                <a href="https://weebcentral.com/series/abc/Other">internal nav</a>
            </section>
        </body></html>
        """;

        var settings = CreateSettings();
        var weebCentral = new WeebCentral(settings, CreateMockClient(html).Object);

        var result = await weebCentral.GetMangaFromId("01J76XYBR7JHFW7Q80MHJP5VYW");

        Assert.NotNull(result);
        var links = result.Value.Item1.Links;
        Assert.Contains(links, l => l.LinkProvider == "AniList" && l.LinkUrl == "https://anilist.co/manga/87170");
        Assert.Contains(links, l => l.LinkProvider == "MangaUpdates" && l.LinkUrl.Contains("eur1ktv"));
        // Internal navigation links are not external trackers and must not be captured.
        Assert.DoesNotContain(links, l => l.LinkUrl.Contains("weebcentral.com"));
    }

    [Fact]
    public async Task GetMangaFromId_IgnoresGenericTrackerLinks_KeepingOnlyEntryUrls()
    {
        // A footer/home link to a tracker must not be mistaken for the series' identity link — only an
        // entry URL (e.g. /manga/<id>) is a real cross-reference.
        var html = """
        <html><head><title>Fire Punch | Weeb Central</title></head>
        <body>
            <a href="https://anilist.co/manga/87170/Fire-Punch">AniList</a>
            <a href="https://anilist.co/forum/recent">AniList Forums</a>
            <a href="https://anilist.co">AniList Home</a>
        </body></html>
        """;

        var settings = CreateSettings();
        var weebCentral = new WeebCentral(settings, CreateMockClient(html).Object);

        var result = await weebCentral.GetMangaFromId("wc-1");

        Assert.NotNull(result);
        var aniListLinks = result.Value.Item1.Links.Where(l => l.LinkProvider == "AniList").ToList();
        Assert.Single(aniListLinks);
        Assert.Equal("https://anilist.co/manga/87170/Fire-Punch", aniListLinks[0].LinkUrl);
    }

    [Fact]
    public async Task GetMangaFromId_CapturesEntryUrlsForEachProvider_AndRejectsGenericOnes()
    {
        var html = """
        <html><head><title>X | Weeb Central</title></head>
        <body>
            <a href="https://anilist.co/manga/87170">al</a>
            <a href="https://myanimelist.net/manga/98270">mal</a>
            <a href="https://www.mangaupdates.com/series/eur1ktv">mu</a>
            <a href="https://www.anime-planet.com/manga/fire-punch">ap</a>
            <a href="https://www.mangaupdates.com/seriesranking">generic mu</a>
            <a href="https://myanimelist.net/forum">generic mal</a>
        </body></html>
        """;

        var settings = CreateSettings();
        var weebCentral = new WeebCentral(settings, CreateMockClient(html).Object);

        var result = await weebCentral.GetMangaFromId("wc-1");

        Assert.NotNull(result);
        var providers = result.Value.Item1.Links.Select(l => l.LinkProvider).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { "AniList", "Anime Planet", "MangaUpdates", "MyAnimeList" }, providers);
    }

    [Fact]
    public async Task GetChapterImageUrls_RequestsImagesPartial_AndExtractsPageImages()
    {
        // WeebCentral defers chapter images to the /chapters/{id}/images HTMX partial; scraping the
        // bare chapter page yields no <img> tags (the empty-stub bug). This runs the real parser
        // against fixture HTML and asserts both the URL and the extraction.
        var imagesPartial = """
        <section>
            <img alt="Page 1" src="https://cdn.example/manga/0001.png" />
            <img alt="Page 2" src="https://cdn.example/manga/0002.png" />
            <img alt="Cover" src="https://cdn.example/cover.jpg" />
        </section>
        """;

        string? requestedUrl = null;
        var mockClient = new Mock<IHttpRequester>();
        mockClient
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .Callback<string, RequestType, string?, CancellationToken?>((url, _, _, _) => requestedUrl = url)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(imagesPartial, Encoding.UTF8, "text/html")
            });

        var settings = CreateSettings();
        var weebCentral = new WeebCentral(settings, mockClient.Object);

        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], []);
        var chapter = new Chapter(manga, "1", null, "Title");
        var chapterId = new SourceId<Chapter>(chapter, weebCentral, "chap-1", "https://weebcentral.com/chapters/chap-1", true);

        var imageUrls = await weebCentral.GetChapterImageUrls(chapterId);

        // Only the alt="Page N" images are extracted (not the cover).
        Assert.Equal(2, imageUrls.Length);
        Assert.Equal("https://cdn.example/manga/0001.png", imageUrls[0]);
        Assert.Equal("https://cdn.example/manga/0002.png", imageUrls[1]);

        // The request must target the images partial, not the bare chapter page.
        Assert.NotNull(requestedUrl);
        Assert.Contains("/chapters/chap-1/images", requestedUrl!);
    }
}
