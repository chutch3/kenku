using API.HttpRequesters.Interfaces;
using System.Net;
using System.Text;
using API;
using API.Acquirers.Interfaces;
using API.Connectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using Moq;
using Xunit;

namespace API.Tests.Unit.Connectors;

public class GetComicsTests
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

    // Synthetic fixtures matching the live selectors (grounded 2026-06-10): results live in a
    // section whose class list contains "post-list"; each post is an <article> with a cover img in
    // .post-header-image and its link in h1.post-title a. The no-results page keeps the post-list
    // section but holds zero articles.
    private static string SearchPage(params string[] articles) => $"""
        <html><body>
        <section class="page-contents post-list post-list-masonry"><div class="post-list-posts">
        {string.Join("\n", articles)}
        </div></section>
        </body></html>
        """;

    private static string Article(string url, string title, string cover = "https://img.test/cover.jpg") => $"""
        <article>
          <div class="post-header-image"><a href="{url}"><img src="{cover}" alt="{title}"></a></div>
          <div class="post-info">
            <h1 class="post-title"><a href="{url}">{title}</a></h1>
            <p class="post-excerpt"><strong>Year : </strong>2026 | <strong>Size :</strong> 91 MB</p>
          </div>
        </article>
        """;

    private static GetComics CreateConnector(Func<string, HttpResponseMessage> route) =>
        new(CreateSettings(), RoutedClient(route).Object);

    [Fact]
    public async Task SearchManga_CollapsesPostsIntoSeriesByParsedTitle_WithCoverAndYear()
    {
        string html = SearchPage(
            Article("https://getcomics.org/other-comics/bb-9/", "Invincible Universe &#8211; Battle Beast #9 (2026)", "https://img.test/bb9.jpg"),
            Article("https://getcomics.org/other-comics/bb-8/", "Invincible Universe &#8211; Battle Beast #8 (2026)", "https://img.test/bb8.jpg"),
            Article("https://getcomics.org/other-comics/saga-60/", "Saga #60 (2024)", "https://img.test/saga.jpg"));
        var connector = CreateConnector(_ => Html(html));

        var results = await connector.SearchManga("battle beast");

        Assert.Equal(2, results.Length);
        var battleBeast = Assert.Single(results, r => r.Item1.Name == "Invincible Universe – Battle Beast");
        Assert.Equal((uint)2026, battleBeast.Item1.Year);
        Assert.Equal("https://img.test/bb9.jpg", battleBeast.Item1.CoverUrl);
        Assert.Equal("Invincible Universe – Battle Beast", battleBeast.Item2.IdOnConnectorSite);
        var saga = Assert.Single(results, r => r.Item1.Name == "Saga");
        Assert.Equal("https://img.test/saga.jpg", saga.Item1.CoverUrl);
    }

    [Fact]
    public async Task SearchManga_ReturnsEmpty_WhenTheNoResultsPageIsServed()
    {
        // A search with no hits still renders the post-list section, just with zero articles —
        // that is a legitimate empty result, not a parse failure.
        var connector = CreateConnector(_ => Html(SearchPage()));

        var results = await connector.SearchManga("zzzznoresults");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchManga_Throws_WhenThePostListSectionIsMissing()
    {
        // A page without the post-list section means the selectors drifted or an error page was
        // served — silently returning [] here is the "I am a hero" bug, so it must be loud.
        var connector = CreateConnector(_ => Html("<html><body><p>maintenance</p></body></html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.SearchManga("saga"));
    }

    [Fact]
    public async Task SearchManga_Throws_WhenTheRequestFails()
    {
        var connector = CreateConnector(_ => Html("", HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.SearchManga("saga"));
    }

    [Fact]
    public async Task GetChapters_MapsTheSeriesPostsToChapters_SkippingOtherSeriesAndNumberlessPosts()
    {
        string page1 = SearchPage(
            Article("https://getcomics.org/other-comics/bb-9/", "Invincible Universe &#8211; Battle Beast #9 (2026)"),
            Article("https://getcomics.org/other-comics/bb-8/", "Invincible Universe &#8211; Battle Beast #8 (2026)"),
            Article("https://getcomics.org/other-comics/saga-60/", "Saga #60 (2024)"),
            Article("https://getcomics.org/other-comics/bb-comp/", "Invincible Universe &#8211; Battle Beast Compendium (2026)"));
        var connector = CreateConnector(url =>
            url.Contains("/page/") ? Html("", HttpStatusCode.NotFound) : Html(page1));
        var mangaId = SeriesId(connector, "Invincible Universe – Battle Beast");

        var chapters = await connector.GetChapters(mangaId);

        Assert.Equal(2, chapters.Length);
        var nine = Assert.Single(chapters, c => c.Item1.ChapterNumber == "9");
        Assert.Equal("https://getcomics.org/other-comics/bb-9/", nine.Item2.WebsiteUrl);
        Assert.Equal("Invincible Universe – Battle Beast #9 (2026)", nine.Item1.Title);
        Assert.Single(chapters, c => c.Item1.ChapterNumber == "8");
    }

    [Fact]
    public async Task GetChapters_WalksThePagedSearch_UntilThePagesRunOut()
    {
        var requested = new List<string>();
        string page1 = SearchPage(Article("https://getcomics.org/c/saga-60/", "Saga #60 (2024)"));
        string page2 = SearchPage(Article("https://getcomics.org/c/saga-61/", "Saga #61 (2024)"));
        var connector = CreateConnector(url =>
        {
            requested.Add(url);
            if (url.Contains("/page/2/")) return Html(page2);
            if (url.Contains("/page/")) return Html("", HttpStatusCode.NotFound);
            return Html(page1);
        });
        var mangaId = SeriesId(connector, "Saga");

        var chapters = await connector.GetChapters(mangaId);

        Assert.Equal(2, chapters.Length);
        Assert.Contains(requested, u => u.StartsWith("https://getcomics.org/?s="));
        Assert.Contains(requested, u => u.StartsWith("https://getcomics.org/page/2/?s="));
    }

    [Fact]
    public async Task GetChapters_DeduplicatesPostsThatParseToTheSameIssue()
    {
        string page = SearchPage(
            Article("https://getcomics.org/c/saga-60/", "Saga #60 (2024)"),
            Article("https://getcomics.org/c/saga-060-hd/", "Saga 060 (2024)"));
        var connector = CreateConnector(url =>
            url.Contains("/page/") ? Html("", HttpStatusCode.NotFound) : Html(page));
        var mangaId = SeriesId(connector, "Saga");

        var chapters = await connector.GetChapters(mangaId);

        var only = Assert.Single(chapters);
        Assert.Equal("60", only.Item1.ChapterNumber);
        Assert.Equal("https://getcomics.org/c/saga-60/", only.Item2.WebsiteUrl);
    }

    [Fact]
    public async Task GetChapters_Throws_WhenTheFirstPageHasNoPostList()
    {
        var connector = CreateConnector(_ => Html("<html><body>cloudflare interstitial</body></html>"));
        var mangaId = SeriesId(connector, "Saga");

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.GetChapters(mangaId));
    }

    [Fact]
    public async Task GetMangaFromId_ReturnsTheCollapsedSeries_WithItsCover()
    {
        // The add flow re-fetches the series by id (the collapsed title); losing the cover here
        // would leave every added comic showing the default logo.
        string html = SearchPage(
            Article("https://getcomics.org/c/saga-60/", "Saga #60 (2024)", "https://img.test/saga.jpg"));
        var connector = CreateConnector(_ => Html(html));

        var result = await connector.GetMangaFromId("Saga");

        Assert.NotNull(result);
        Assert.Equal("Saga", result.Value.Item1.Name);
        Assert.Equal("https://img.test/saga.jpg", result.Value.Item1.CoverUrl);
    }

    [Fact]
    public async Task GetMangaFromId_ReturnsNull_WhenNoPostsCollapseToTheId()
    {
        var connector = CreateConnector(_ => Html(SearchPage()));

        Assert.Null(await connector.GetMangaFromId("Saga"));
    }

    [Fact]
    public async Task GetMangaFromUrl_ParsesThePostPage_IntoItsCollapsedSeries()
    {
        string postHtml = """
            <html><head>
            <meta property="og:image" content="https://img.test/bb9-cover.jpg" />
            </head><body>
            <h1 class="post-title">Invincible Universe &#8211; Battle Beast #9 (2026)</h1>
            </body></html>
            """;
        var connector = CreateConnector(_ => Html(postHtml));

        var result = await connector.GetMangaFromUrl("https://getcomics.org/other-comics/bb-9/");

        Assert.NotNull(result);
        Assert.Equal("Invincible Universe – Battle Beast", result.Value.Item1.Name);
        Assert.Equal("https://img.test/bb9-cover.jpg", result.Value.Item1.CoverUrl);
        Assert.Equal((uint)2026, result.Value.Item1.Year);
    }

    [Fact]
    public async Task PageImagePaths_AreNotSupported()
    {
        var connector = CreateConnector(_ => Html(""));
        var mangaId = SeriesId(connector, "Saga");
        var chapter = new Chapter(mangaId.Obj, "1", null, null);
        var chapterId = new SourceId<Chapter>(chapter, connector, "saga-1", "https://getcomics.org/c/saga-1/", true);

        await Assert.ThrowsAsync<NotSupportedException>(() => connector.GetChapterImageUrls(chapterId));
        await Assert.ThrowsAsync<NotSupportedException>(() => connector.DownloadImage("https://img.test/x.jpg", CancellationToken.None));
    }

    // Post-page fixtures matching the live structure (grounded 2026-06-10): download buttons are
    // <a title="…"> inside .aio-button-center divs; the Main Server button's title casing varies
    // ("DOWNLOAD NOW" / "Download Now"); mirror buttons name their host.
    private static string PostPage(params string[] buttons) => $"""
        <html><body>
        <h1 class="post-title">Saga #60 (2024)</h1>
        <section class="post-contents">
        {string.Join("\n", buttons)}
        </section>
        </body></html>
        """;

    private static string Button(string title, string href) => $"""
        <div class="aio-button-center"><div class="aio-pulse"><a href="{href}" class="aio-red" title="{title}" rel="nofollow">{title}</a></div></div>
        """;

    private static SourceId<Chapter> ChapterId(SeriesSource connector, string postUrl)
    {
        var manga = new Series("Saga", "", "", SeriesReleaseStatus.Continuing, [], [], [], [], originalLanguage: "en");
        var chapter = new Chapter(manga, "60", null, null);
        var id = new SourceId<Chapter>(chapter, connector, "60", postUrl, true);
        chapter.SourceIds.Add(id);
        return id;
    }

    [Fact]
    public async Task ResolveArchiveUrl_PrefersTheMainServerLink()
    {
        string html = PostPage(
            Button("DOWNLOAD NOW", "https://getcomics.org/dls/abc123"),
            Button("TERABOX", "https://1024terabox.com/s/xyz"),
            Button("PIXELDRAIN", "https://getcomics.org/dls/pd456"));
        var connector = CreateConnector(_ => Html(html));

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        Assert.Equal("https://getcomics.org/dls/abc123", Assert.IsType<ArchiveResolution.Resolved>(resolution).Url);
    }

    [Fact]
    public async Task ResolveArchiveUrl_ResolvesPixeldrain_WhenThereIsNoMainServer()
    {
        // The Pixeldrain button is a getcomics dls wrapper that 302s to the share page; the direct
        // file lives at /api/file/{id}. Following the redirect and rewriting gets a fetchable URL.
        string html = PostPage(
            Button("TERABOX", "https://1024terabox.com/s/xyz"),
            Button("PIXELDRAIN", "https://getcomics.org/dls/pd456"));
        var connector = CreateConnector(url =>
        {
            if (url == "https://getcomics.org/dls/pd456")
            {
                var landed = Html("<html><body>pixeldrain landing</body></html>");
                landed.RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://pixeldrain.com/u/iRQ3ndKZ");
                return landed;
            }
            return Html(html);
        });

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        Assert.Equal("https://pixeldrain.com/api/file/iRQ3ndKZ?download",
            Assert.IsType<ArchiveResolution.Resolved>(resolution).Url);
    }

    [Fact]
    public async Task ResolveArchiveUrl_ResolvesMediafire_FromItsDownloadButton()
    {
        string html = PostPage(Button("MEDIAFIRE", "https://getcomics.org/dls/mf789"));
        string mediafirePage = """
            <html><body><a id="downloadButton" href="https://download123.mediafire.com/file/saga60.cbz">Download</a></body></html>
            """;
        var connector = CreateConnector(url =>
            url == "https://getcomics.org/dls/mf789" ? Html(mediafirePage) : Html(html));

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        Assert.Equal("https://download123.mediafire.com/file/saga60.cbz",
            Assert.IsType<ArchiveResolution.Resolved>(resolution).Url);
    }

    [Fact]
    public async Task ResolveArchiveUrl_ParksMirrorOnlyPosts_NamingTheHosts()
    {
        string html = PostPage(
            Button("TERABOX", "https://1024terabox.com/s/xyz"),
            Button("MEGA", "https://mega.nz/file/abc"),
            Button("Read Online", "https://readcomicsonline.ru/comic/saga/60"));
        var connector = CreateConnector(_ => Html(html));

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        var manual = Assert.IsType<ArchiveResolution.Manual>(resolution);
        Assert.Contains("TERABOX", manual.Reason);
        Assert.Contains("MEGA", manual.Reason);
        Assert.Contains("download manually", manual.Reason);
        Assert.DoesNotContain("Read Online", manual.Reason);
    }

    [Fact]
    public async Task ResolveArchiveUrl_ParksPostsThatBundleSeveralDownloads()
    {
        // A multi-single post carries one Download Now per bundled issue; fetching just the first
        // would silently drop the rest, so the whole post goes to manual handling instead.
        string html = PostPage(
            Button("Download Now", "https://getcomics.org/dls/one"),
            Button("DOWNLOAD NOW", "https://getcomics.org/dls/two"));
        var connector = CreateConnector(_ => Html(html));

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        Assert.Contains("2 separate downloads", Assert.IsType<ArchiveResolution.Manual>(resolution).Reason);
    }

    [Fact]
    public async Task ResolveArchiveUrl_ParksPostsWithNoDownloadButtons()
    {
        // Real case: a post whose hosts were all taken down keeps only a Read Online button.
        string html = PostPage(Button("Read Online", "https://readcomicsonline.ru/comic/saga/60"));
        var connector = CreateConnector(_ => Html(html));

        var resolution = await connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None);

        Assert.IsType<ArchiveResolution.Manual>(resolution);
    }

    [Fact]
    public async Task ResolveArchiveUrl_Throws_WhenThePageIsNotAPost()
    {
        var connector = CreateConnector(_ => Html("<html><body>cloudflare interstitial</body></html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            connector.ResolveArchiveUrl(ChapterId(connector, "https://getcomics.org/c/saga-60/"), CancellationToken.None));
    }

    private static SourceId<Series> SeriesId(SeriesSource connector, string title)
    {
        var manga = new Series(title, "", "", SeriesReleaseStatus.Continuing, [], [], [], [], originalLanguage: "en");
        var id = new SourceId<Series>(manga, connector, title, null, true);
        manga.SourceIds.Add(id);
        return id;
    }
}
