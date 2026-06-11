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

public class ComicHubFreeTests
{
    private static KenkuSettings CreateSettings() => new KenkuSettings();

    private static Mock<IHttpRequester> RoutedClient(Func<string, HttpResponseMessage> route)
    {
        var mockClient = new Mock<IHttpRequester>();
        mockClient
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync((string url, RequestType _, string? _, CancellationToken? _) => route(url));
        return mockClient;
    }

    private static HttpResponseMessage Html(string content, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(content, Encoding.UTF8, "text/html") };

    private static ComicHubFree CreateConnector(Func<string, HttpResponseMessage> route) =>
        new(CreateSettings(), RoutedClient(route).Object);

    // Fixtures mirror the live site (grounded 2026-06-11): search results are div.cartoon-box cards
    // with an h3 link and a lazyloaded cover; the no-results page keeps one cartoon-box whose h3
    // has no link. The series page lists issues in div.episode-list > table rows.
    private static string SearchCard(string slug, string name, string cover) => $"""
        <div class="cartoon-box">
          <a href="https://comichubfree.com/comic/{slug}" class="image">
            <img class="lazyload" src="data:image/png;base64,x" data-src="{cover}" alt="{name}">
          </a>
          <div class="mb-right">
            <h3><a href="https://comichubfree.com/comic/{slug}" title="Read {name} online">{name}</a></h3>
            <div class="detail">72 Issue(s) Published</div>
            <div class="detail"> Status: Completed</div>
            <div class="detail">Released: 2006</div>
          </div>
        </div>
        """;

    private static string SearchPage(params string[] cards) => $"""
        <html><body><div class="movie-list-index home-v2">
        {string.Join("\n", cards)}
        </div></body></html>
        """;

    private const string NoResultsPage = """
        <html><body><div class="movie-list-index home-v2">
        <div class="cartoon-box"><div class="mb-right">
        <h3>No results found for "zzz"</h3>
        <p>Sorry, we couldn't find any comics matching "<strong>zzz</strong>".</p>
        </div></div>
        </div></body></html>
        """;

    private static string SeriesPage(string seriesName, string issueRows, string? nextPageHref = null) => $"""
        <html><head><meta property="og:image" content="https://comichubfree.com/images/thumbnail/the-boys.jpeg" /></head>
        <body>
        <h1><span class="title-1">Read {seriesName} Comics Online for Free</span></h1>
        <div class="episode-list">
          <h2 class="episode-list-info">Explore Chapters of {seriesName}</h2>
          <div><table class="table"><tbody id="list">
          {issueRows}
          </tbody></table></div>
        </div>
        {(nextPageHref is null ? "" : $"""<ul class="pagination"><li class="active"><span>1</span></li><li><a href="{nextPageHref}">2</a></li></ul>""")}
        </body></html>
        """;

    private static string IssueRow(string slug, string issue, string text) => $"""
        <tr><td><a href="https://comichubfree.com/{slug}/issue-{issue}">{text}</a></td><td>06/11/2026</td></tr>
        """;

    private static SourceId<Series> SeriesId(SeriesSource connector, string name, string slug)
    {
        var manga = new Series(name, "", "", SeriesReleaseStatus.Continuing, [], [], [], [], originalLanguage: "en");
        var id = new SourceId<Series>(manga, connector, slug, $"https://comichubfree.com/comic/{slug}", true);
        manga.SourceIds.Add(id);
        return id;
    }

    [Fact]
    public async Task SearchManga_ParsesResultCards()
    {
        string html = SearchPage(
            SearchCard("the-boys", "The Boys", "https://comichubfree.com/images/thumbnail/the-boys.jpeg"),
            SearchCard("invincible", "Invincible (2003)", "https://comichubfree.com/images/thumbnail/invincible.jpeg"));
        string? requestedUrl = null;
        var connector = CreateConnector(url => { requestedUrl = url; return Html(html); });

        var results = await connector.SearchManga("the boys");

        Assert.Equal(2, results.Length);
        var boys = Assert.Single(results, r => r.Item1.Name == "The Boys");
        Assert.Equal("the-boys", boys.Item2.IdOnConnectorSite);
        Assert.Equal("https://comichubfree.com/images/thumbnail/the-boys.jpeg", boys.Item1.CoverUrl);
        Assert.Equal((uint)2006, boys.Item1.Year);
        Assert.Single(results, r => r.Item1.Name == "Invincible (2003)");
        Assert.Equal("https://comichubfree.com/search-comic?key=the+boys", requestedUrl);
    }

    [Fact]
    public async Task SearchManga_ReturnsEmpty_OnTheNoResultsPage()
    {
        var connector = CreateConnector(_ => Html(NoResultsPage));

        Assert.Empty(await connector.SearchManga("zzz"));
    }

    [Fact]
    public async Task SearchManga_Throws_WhenTheResultsStructureIsMissing()
    {
        var connector = CreateConnector(_ => Html("<html><body><p>maintenance</p></body></html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.SearchManga("saga"));
    }

    [Fact]
    public async Task GetMangaFromId_ParsesTheSeriesPage()
    {
        var connector = CreateConnector(_ => Html(SeriesPage("The Boys", IssueRow("the-boys", "1", "The Boys Issue #1"))));

        var result = await connector.GetMangaFromId("the-boys");

        Assert.NotNull(result);
        Assert.Equal("The Boys", result.Value.Item1.Name);
        Assert.Equal("https://comichubfree.com/images/thumbnail/the-boys.jpeg", result.Value.Item1.CoverUrl);
        Assert.Equal("the-boys", result.Value.Item2.IdOnConnectorSite);
    }

    [Fact]
    public async Task GetMangaFromUrl_ResolvesTheSlug()
    {
        var connector = CreateConnector(_ => Html(SeriesPage("The Boys", IssueRow("the-boys", "1", "The Boys Issue #1"))));

        var result = await connector.GetMangaFromUrl("https://comichubfree.com/comic/the-boys");

        Assert.NotNull(result);
        Assert.Equal("The Boys", result.Value.Item1.Name);
    }

    [Fact]
    public async Task GetChapters_ParsesTheEpisodeList_SkippingUnnumberedIssues()
    {
        string rows = IssueRow("the-boys", "72", "The Boys Issue #72")
                      + IssueRow("the-boys", "71", "The Boys Issue #71")
                      + """<tr><td><a href="https://comichubfree.com/the-boys/issue-annual">The Boys Annual</a></td></tr>""";
        var connector = CreateConnector(_ => Html(SeriesPage("The Boys", rows)));
        var mangaId = SeriesId(connector, "The Boys", "the-boys");

        var chapters = await connector.GetChapters(mangaId);

        Assert.Equal(2, chapters.Length);
        var last = Assert.Single(chapters, c => c.Item1.ChapterNumber == "72");
        Assert.Equal("https://comichubfree.com/the-boys/issue-72", last.Item2.WebsiteUrl);
        Assert.Equal("The Boys Issue #72", last.Item1.Title);
    }

    [Fact]
    public async Task GetChapters_WalksThePaginatedIssueList()
    {
        string page1 = SeriesPage("The Boys", IssueRow("the-boys", "72", "The Boys Issue #72"),
            nextPageHref: "the-boys?page=2");
        string page2 = SeriesPage("The Boys", IssueRow("the-boys", "1", "The Boys Issue #1"));
        var requested = new List<string>();
        var connector = CreateConnector(url =>
        {
            requested.Add(url);
            return url.Contains("page=2") ? Html(page2) : Html(page1);
        });
        var mangaId = SeriesId(connector, "The Boys", "the-boys");

        var chapters = await connector.GetChapters(mangaId);

        Assert.Equal(2, chapters.Length);
        Assert.Contains(requested, u => u.EndsWith("/comic/the-boys"));
        Assert.Contains(requested, u => u.EndsWith("/comic/the-boys?page=2"));
    }

    [Fact]
    public async Task GetChapters_Throws_WhenTheEpisodeListIsMissing()
    {
        var connector = CreateConnector(_ => Html("<html><body>interstitial</body></html>"));
        var mangaId = SeriesId(connector, "The Boys", "the-boys");

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.GetChapters(mangaId));
    }

    [Fact]
    public async Task GetChapterImageUrls_UsesTheAllPagesView()
    {
        // The per-page reading view shows one image; the /all view lists every page as a lazyloaded
        // img.chapter_img whose real URL is in data-src (src is a base64 placeholder).
        const string allPages = """
            <html><body>
            <img class="chapter_img lazyload" src="data:image/png;base64,x" data-src="https://comichubfree.com/the-boys/issue-1/225753/1.jpg" alt="The Boys Issue #1 - Page 1">
            <img class="chapter_img lazyload" src="data:image/png;base64,x" data-src="https://comichubfree.com/the-boys/issue-1/225753/2.jpg" alt="The Boys Issue #1 - Page 2">
            <img class="other" src="https://comichubfree.com/images/site/logo.png">
            </body></html>
            """;
        string? requestedUrl = null;
        var connector = CreateConnector(url => { requestedUrl = url; return Html(allPages); });
        var manga = new Series("The Boys", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], originalLanguage: "en");
        var chapter = new Chapter(manga, "1", null, null);
        var chapterId = new SourceId<Chapter>(chapter, connector, "issue-1", "https://comichubfree.com/the-boys/issue-1", true);

        var imageUrls = await connector.GetChapterImageUrls(chapterId);

        Assert.Equal(2, imageUrls.Length);
        Assert.Equal("https://comichubfree.com/the-boys/issue-1/225753/1.jpg", imageUrls[0]);
        Assert.Equal("https://comichubfree.com/the-boys/issue-1/225753/2.jpg", imageUrls[1]);
        Assert.Equal("https://comichubfree.com/the-boys/issue-1/all", requestedUrl);
    }
}
