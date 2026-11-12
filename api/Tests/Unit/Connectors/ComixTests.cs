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

public class ComixTests
{
    // Trimmed from the live title page (grounded 2026-11-11): the series page is server-rendered and
    // embeds its metadata as JSON in <script id="initial-data">, keyed by a React Query cache key.
    private const string TitlePageHtml = """
    <html><head><title>Star-Embracing Swordmaster - Comix</title></head>
    <body>
    <script id="initial-data" type="application/json">{"page":"manga","queries":{
      "[\"manga\",\"detail\",\"0j5d\"]":{
        "id":13769,"hid":"0j5d","title":"Star-Embracing Swordmaster",
        "altTitles":["Byeoreul Pumeun Swordmaster","The Stellar Swordmaster"],
        "type":"manhwa","status":"releasing","originalLanguage":"ko",
        "poster":{"medium":"/images/covers/8e/39/8e39@280.jpg","large":"/images/covers/8e/39/8e39.jpg"},
        "year":2023,
        "synopsis":"The so-called \"Lighthouse of the North\" hides a dark underbelly.",
        "links":{"al":"https://anilist.co/manga/170400","mal":"https://myanimelist.net/manga/163116",
                 "mu":"https://www.mangaupdates.com/series/cnro3u3","mb":"https://mangabaka.org/588"},
        "url":"/title/0j5d-star-embracing-swordmaster",
        "genres":[{"id":6,"title":"Action","slug":"action"},{"id":12,"title":"Fantasy","slug":"fantasy"}],
        "authors":[{"id":14547,"title":"Hong Dae Eui","slug":"hong-dae-eui"}],
        "artists":[{"id":14549,"title":"Juno","slug":"juno"}]
      },
      "[\"manga\",\"groups\",\"0j5d\"]":[]
    }}</script>
    </body></html>
    """;

    private static Mock<IHttpRequester> CreateMockClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var mockClient = new Mock<IHttpRequester>();
        mockClient
            .Setup(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(() => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, "text/html")
            });
        return mockClient;
    }

    private static Comix CreateComix(Mock<IHttpRequester> client, Mock<IHeadlessBrowser>? browser = null) =>
        new(new KenkuSettings(), client.Object, (browser ?? new Mock<IHeadlessBrowser>()).Object);

    private static Mock<IHeadlessBrowser> CreateMockBrowser(string renderedHtml)
    {
        var browser = new Mock<IHeadlessBrowser>();
        browser
            .Setup(b => b.RenderHtmlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(renderedHtml);
        return browser;
    }

    // Trimmed from the rendered /browse?keyword= page (grounded 2026-11-11). Results come from the
    // signed API, so the page must be rendered; each .lrow card carries enough for a search result.
    private const string BrowsePageHtml = """
    <html><body><main>
    <div class="list-toolbar"><div class="list-count mono"><strong>2</strong> items</div></div>
    <div class="lrow"><a class="lrow__poster" href="/title/0j5d-star-embracing-swordmaster" aria-label="Star-Embracing Swordmaster"><div class="poster poster--md"><img alt="" loading="lazy" src="/images/covers/8e/39/8e39@280.jpg"></div></a>
      <div class="lrow__body"><a class="lrow__title-link" href="/title/0j5d-star-embracing-swordmaster"><h3 class="lrow__title">Star-Embracing Swordmaster</h3></a>
        <div class="lrow__meta mono"><span class="lrow__rank">#52</span><span class="lrow__type">MANHWA</span><span class="lrow__year">2023</span><span class="lrow__status lrow__status--releasing">RELEASING</span><span class="lrow__ch">CH.146</span></div>
        <p class="lrow__desc">The so-called &quot;Lighthouse of the North,&quot; the prosperous trading town of S</p></div></div>
    <div class="lrow"><a class="lrow__poster" href="/title/gm2lx-solo-swordmaster"><div class="poster poster--md"><img alt="" loading="lazy" src="/images/covers/aa/bb/aabb@280.jpg"></div></a>
      <div class="lrow__body"><a class="lrow__title-link" href="/title/gm2lx-solo-swordmaster"><h3 class="lrow__title">Solo Swordmaster</h3></a>
        <div class="lrow__meta mono"><span class="lrow__type">MANHWA</span><span class="lrow__status lrow__status--finished">FINISHED</span></div></div></div>
    </main></body></html>
    """;

    private const string BrowseEmptyHtml = """
    <html><body><main><div class="list-count mono"><strong>0</strong> items</div>
    <div><div class="list-empty"><p>No comics match your filters.</p></div></div></main></body></html>
    """;

    [Fact]
    public async Task GetSeriesFromUrl_ParsesServerRenderedMetadata()
    {
        var comix = CreateComix(CreateMockClient(""), CreateMockBrowser(TitlePageHtml));

        var result = await comix.GetSeriesFromUrl("https://comix.to/title/0j5d-star-embracing-swordmaster");

        Assert.NotNull(result);
        var (series, sourceId) = result.Value;
        Assert.Equal("Star-Embracing Swordmaster", series.Name);
        Assert.Equal("The so-called \"Lighthouse of the North\" hides a dark underbelly.", series.Description);
        Assert.Equal("https://comix.to/images/covers/8e/39/8e39.jpg", series.CoverUrl);
        Assert.Equal(SeriesReleaseStatus.Continuing, series.ReleaseStatus);
        Assert.Equal(2023u, series.Year);
        Assert.Equal("ko", series.OriginalLanguage);
        Assert.Equal(["Hong Dae Eui", "Juno"], series.Authors.Select(a => a.AuthorName));
        Assert.Equal(["Action", "Fantasy"], series.SeriesTags.Select(t => t.Tag));
        Assert.Equal(["Byeoreul Pumeun Swordmaster", "The Stellar Swordmaster"], series.AltTitles.Select(a => a.Title));
        Assert.Contains(series.Links, l => l.LinkProvider == "AniList" && l.LinkUrl == "https://anilist.co/manga/170400");
        Assert.Contains(series.Links, l => l.LinkProvider == "MyAnimeList" && l.LinkUrl == "https://myanimelist.net/manga/163116");
        Assert.Contains(series.Links, l => l.LinkProvider == "MangaUpdates" && l.LinkUrl == "https://www.mangaupdates.com/series/cnro3u3");

        Assert.Equal("0j5d", sourceId.IdOnConnectorSite);
        Assert.Equal("https://comix.to/title/0j5d-star-embracing-swordmaster", sourceId.WebsiteUrl);
    }

    [Fact]
    public async Task GetSeriesFromUrl_AcceptsAChapterUrl()
    {
        var browser = CreateMockBrowser(TitlePageHtml);
        var comix = CreateComix(CreateMockClient(""), browser);

        var result = await comix.GetSeriesFromUrl("https://comix.to/title/0j5d-star-embracing-swordmaster/8123456-chapter-12");

        Assert.NotNull(result);
        Assert.Equal("0j5d", result.Value.Item2.IdOnConnectorSite);
        browser.Verify(b => b.RenderHtmlAsync("https://comix.to/title/0j5d", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData("https://weebcentral.com/series/abc/foo")]
    [InlineData("https://comix.to/browse")]
    public async Task GetSeriesFromUrl_RejectsNonTitleUrls(string url)
    {
        var browser = CreateMockBrowser(TitlePageHtml);
        var comix = CreateComix(CreateMockClient(""), browser);

        Assert.Null(await comix.GetSeriesFromUrl(url));
        browser.Verify(b => b.RenderHtmlAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSeriesFromId_RendersTheTitlePageInTheBrowser()
    {
        // comix.to's Cloudflare blocks plain HTTP clients from the homelab's VPN exit IP
        // while letting the headless browser through, so even the server-rendered title page is fetched
        // through the browser.
        var client = CreateMockClient(TitlePageHtml);
        var browser = CreateMockBrowser(TitlePageHtml);
        var comix = CreateComix(client, browser);

        var result = await comix.GetSeriesFromId("0j5d");

        Assert.NotNull(result);
        Assert.Equal("Star-Embracing Swordmaster", result.Value.Item1.Name);
        browser.Verify(b => b.RenderHtmlAsync("https://comix.to/title/0j5d", null, "#initial-data", It.IsAny<string?>(), It.IsAny<CancellationToken>()));
        client.Verify(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()), Times.Never);
    }

    [Fact]
    public async Task GetSeriesFromId_ReturnsNullWhenThePageHasNoInitialData()
    {
        var comix = CreateComix(CreateMockClient(""), CreateMockBrowser("<html><head><title>Attention Required! | Cloudflare</title></head></html>"));

        Assert.Null(await comix.GetSeriesFromId("0j5d"));
    }

    [Theory]
    [InlineData("releasing", SeriesReleaseStatus.Continuing)]
    [InlineData("finished", SeriesReleaseStatus.Completed)]
    [InlineData("on_hiatus", SeriesReleaseStatus.OnHiatus)]
    [InlineData("discontinued", SeriesReleaseStatus.Cancelled)]
    [InlineData("not_yet_released", SeriesReleaseStatus.Unreleased)]
    public async Task GetSeriesFromId_MapsReleaseStatus(string siteStatus, SeriesReleaseStatus expected)
    {
        var html = TitlePageHtml.Replace("\"status\":\"releasing\"", $"\"status\":\"{siteStatus}\"");
        var comix = CreateComix(CreateMockClient(""), CreateMockBrowser(html));

        var result = await comix.GetSeriesFromId("0j5d");

        Assert.Equal(expected, result!.Value.Item1.ReleaseStatus);
    }

    [Fact]
    public async Task SearchSeries_BuildsResultsFromTheRenderedCards_WithoutPerResultRequests()
    {
        var client = CreateMockClient("");
        var browser = CreateMockBrowser(BrowsePageHtml);
        var comix = CreateComix(client, browser);

        var results = await comix.SearchSeries("star embracing");

        Assert.Equal(["Star-Embracing Swordmaster", "Solo Swordmaster"], results.Select(r => r.Item1.Name));
        var (series, id) = results[0];
        Assert.Equal("0j5d", id.IdOnConnectorSite);
        Assert.Equal("https://comix.to/title/0j5d-star-embracing-swordmaster", id.WebsiteUrl);
        Assert.Equal("https://comix.to/images/covers/8e/39/8e39@280.jpg", series.CoverUrl);
        Assert.Equal("The so-called \"Lighthouse of the North,\" the prosperous trading town of S", series.Description);
        Assert.Equal(SeriesReleaseStatus.Continuing, series.ReleaseStatus);
        Assert.Equal(2023u, series.Year);
        Assert.Equal(SeriesReleaseStatus.Completed, results[1].Item1.ReleaseStatus);

        browser.Verify(b => b.RenderHtmlAsync("https://comix.to/browse?keyword=star+embracing&sort=relevance%3Adesc&content_rating=safe%2Csuggestive%2Cpornographic%2Cerotica", null, ".lrow, .list-empty", null, It.IsAny<CancellationToken>()));
        client.Verify(c => c.MakeRequest(It.IsAny<string>(), It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<CancellationToken?>()), Times.Never);
    }

    [Fact]
    public async Task SearchSeries_ReturnsNothingWhenTheSiteReportsNoMatches()
    {
        var comix = CreateComix(CreateMockClient(""), CreateMockBrowser(BrowseEmptyHtml));

        Assert.Empty(await comix.SearchSeries("zzqxvjwk"));
    }

    [Fact]
    public async Task SearchSeries_ThrowsWhenNeitherResultsNorTheEmptyStateRendered()
    {
        // A blocked data API (Cloudflare captcha) or drifted markup leaves the page shell without either;
        // that must surface as a failure, not as "no results".
        var comix = CreateComix(CreateMockClient(""), CreateMockBrowser("<html><body><main></main></body></html>"));

        await Assert.ThrowsAsync<HttpRequestException>(() => comix.SearchSeries("star embracing"));
    }

    // Chapter-list markup from the rendered title page (grounded 2026-11-11). The list is paginated by
    // URL (?page=N), every scan-group upload is its own row, and the footer reports the total.
    private const string SeriesUrl = "https://comix.to/title/0j5d-star-embracing-swordmaster";

    private static string ChapterRow(string chapterId, string label, string? group = "Lagoon Scans", string extraSpans = "") => $"""
        <div class="mchap-row"><a class="mchap-row__primary" href="/title/0j5d-star-embracing-swordmaster/{chapterId}"><span class="mchap-row__ch">{label}</span>{extraSpans}</a>
        <div class="mchap-row__secondary">{(group is null ? "" : $"<a class=\"mchap-row__group\" href=\"/groups/1\"><span>{group}</span></a>")}<span class="mchap-row__time">40m ago</span></div></div>
        """;

    private static string ChapterPage(int from, int to, int total, params string[] rows) => $"""
        <html><body><div class="mchap-list">{string.Concat(rows)}</div>
        <div class="mchap-foot"><span class="mchap-foot__hint">Showing {from} to {to} of {total} items</span></div></body></html>
        """;

    private static Mock<IHeadlessBrowser> CreateChapterBrowser(params string[] pages)
    {
        var browser = new Mock<IHeadlessBrowser>();
        for (int i = 0; i < pages.Length; i++)
        {
            string url = i == 0 ? SeriesUrl : $"{SeriesUrl}?page={i + 1}";
            browser.Setup(b => b.RenderHtmlAsync(url, It.IsAny<string?>(), ".mchap-row", "Comix", It.IsAny<CancellationToken>()))
                .ReturnsAsync(pages[i]);
        }
        return browser;
    }

    private static SourceId<Series> ComixSeries(Comix comix)
    {
        var series = new Series("Star-Embracing Swordmaster", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
        var id = new SourceId<Series>(series, comix, "0j5d", SeriesUrl);
        series.SourceIds.Add(id);
        return id;
    }

    [Fact]
    public async Task GetChapters_ReturnsEveryUploadAcrossAllPages()
    {
        var browser = CreateChapterBrowser(
            ChapterPage(1, 3, 5,
                ChapterRow("11724643-chapter-146", "Ch.146"),
                ChapterRow("11722399-chapter-137", "Ch.137", "WebToon", """<span class="mchap-row__title">S2-137</span>"""),
                ChapterRow("11683283-chapter-98", "Ch.98", "Manga Drama", """<span class="mchap-row__vol">Vol.12</span><span class="mchap-row__title">Parting Words</span>""")),
            ChapterPage(4, 5, 5,
                ChapterRow("11351022-chapter-137", "Ch.137", "Unknown group"),
                ChapterRow("11000001-chapter-10-5", "Ch.10.5", group: null)));
        var comix = CreateComix(CreateMockClient(""), browser);

        var chapters = await comix.GetChapters(ComixSeries(comix));

        var rows = chapters.Select(c => (c.Item1.ChapterNumber, c.Item1.VolumeNumber, c.Item1.Title, c.Item2.IdOnConnectorSite, c.Item2.ScanGroup)).ToArray();
        Assert.Equal(5, rows.Length);
        Assert.Contains(("146", (int?)null, (string?)null, "11724643", "Lagoon Scans"), rows);
        Assert.Contains(("137", (int?)null, "S2-137", "11722399", "WebToon"), rows);
        Assert.Contains(("98", (int?)12, "Parting Words", "11683283", "Manga Drama"), rows);
        Assert.Contains(("137", (int?)null, (string?)null, "11351022", (string?)null), rows);
        Assert.Contains(("10.5", (int?)null, (string?)null, "11000001", (string?)null), rows);
        Assert.Equal($"{SeriesUrl}/11724643-chapter-146", chapters.Single(c => c.Item2.IdOnConnectorSite == "11724643").Item2.WebsiteUrl);
        Assert.Equal(["10.5", "98", "137", "137", "146"], chapters.Select(c => c.Item1.ChapterNumber));
    }

    [Fact]
    public async Task GetChapters_StopsPagingOnceAPageHoldsOnlyKnownUploads()
    {
        // The list is newest-first, so a re-sync can stop at the first page with nothing new on it.
        var browser = CreateChapterBrowser(
            ChapterPage(1, 1, 3, ChapterRow("200-chapter-147", "Ch.147")),
            ChapterPage(2, 2, 3, ChapterRow("100-chapter-146", "Ch.146")),
            ChapterPage(3, 3, 3, ChapterRow("50-chapter-145", "Ch.145")));
        var comix = CreateComix(CreateMockClient(""), browser);
        var seriesId = ComixSeries(comix);
        var known = new Chapter(seriesId.Obj, "146", null);
        known.SourceIds.Add(new SourceId<Chapter>(known, comix, "100", $"{SeriesUrl}/100-chapter-146"));
        seriesId.Obj.Chapters.Add(known);

        var chapters = await comix.GetChapters(seriesId);

        Assert.Contains(chapters, c => c.Item2.IdOnConnectorSite == "200");
        browser.Verify(b => b.RenderHtmlAsync($"{SeriesUrl}?page=3", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChapters_SkipsRowsWithoutANumericChapter()
    {
        var browser = CreateChapterBrowser(ChapterPage(1, 2, 2,
            ChapterRow("300-chapter-1", "Ch.1"),
            ChapterRow("301-oneshot", "Oneshot")));
        var comix = CreateComix(CreateMockClient(""), browser);

        var chapters = await comix.GetChapters(ComixSeries(comix));

        Assert.Equal(["1"], chapters.Select(c => c.Item1.ChapterNumber));
    }

    [Fact]
    public async Task GetChapters_ThrowsWhenTheChapterListDidNotRender()
    {
        // A captcha-blocked data API renders the title page without its chapter list; that must fail the
        // sync instead of reading as a series with no chapters.
        var browser = CreateChapterBrowser("<html><body><h1>Star-Embracing Swordmaster</h1></body></html>");
        var comix = CreateComix(CreateMockClient(""), browser);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => comix.GetChapters(ComixSeries(comix)));
        Assert.Contains("verif", ex.Message);
    }

    // The reader (grounded by the spike, 84/84 pages harvested live) is a virtualized long strip: only the
    // images near the viewport are mounted, so pages must be collected while scrolling down the strip.
    private const string ChapterUrl = "https://comix.to/title/0j5d-star-embracing-swordmaster/11724643-chapter-146";

    private sealed class FakeLongStripReader(int pageCount, bool rendered = true, bool blocked = false, int? canvasAt = null) : IBrowserPage
    {
        private const int PageHeight = 1200, Viewport = 900;
        private int _scrollTop;

        public static string ImageUrl(int i) => $"https://wyr.example{i % 3}.site/hi/token{i}";

        public Task<bool> WaitForSelectorAsync(string selector) => Task.FromResult(rendered || blocked);

        public Task<IReadOnlyList<string>> QueryAttributesAsync(string selector, string attribute)
        {
            IReadOnlyList<string> result = selector switch
            {
                "main.rpage-main--long-strip" => rendered ? ["rpage-main rpage-main--long-strip"] : [],
                ".rpage-empty" => blocked ? ["rpage-empty"] : [],
                "main.rpage-main--long-strip img.rpage-page__img" => Mounted().Where(i => i != canvasAt).Select(ImageUrl).Prepend("data:image/gif;base64,R0lGOD").ToList(),
                "main.rpage-main--long-strip canvas" => Mounted().Where(i => i == canvasAt).Select(_ => "").ToList(),
                _ => [],
            };
            return Task.FromResult(result);
        }

        public Task<bool> ScrollByAsync(string? containerSelector, int pixels, int settleMs)
        {
            if (containerSelector is null)
                return Task.FromResult(true); // the document itself doesn't scroll; the strip does
            _scrollTop = Math.Min(_scrollTop + pixels, Math.Max(0, pageCount * PageHeight - Viewport));
            return Task.FromResult(_scrollTop + Viewport >= pageCount * PageHeight);
        }

        // Pages overlapping the viewport, plus one either side.
        private IEnumerable<int> Mounted()
        {
            int first = Math.Max(0, _scrollTop / PageHeight - 1);
            int last = Math.Min(pageCount - 1, (_scrollTop + Viewport) / PageHeight + 1);
            return Enumerable.Range(first, last - first + 1);
        }
    }

    private static Mock<IHeadlessBrowser> CreateReaderBrowser(IBrowserPage reader)
    {
        var browser = new Mock<IHeadlessBrowser>();
        browser.Setup(b => b.DrivePageAsync(ChapterUrl, It.IsAny<Func<IBrowserPage, Task<string[]>>>(), "Comix", It.IsAny<CancellationToken>()))
            .Returns((string _, Func<IBrowserPage, Task<string[]>> script, string? _, CancellationToken _) => script(reader));
        return browser;
    }

    private static SourceId<Chapter> ComixChapter(Comix comix)
    {
        var chapter = new Chapter(ComixSeries(comix).Obj, "146", null);
        return new SourceId<Chapter>(chapter, comix, "11724643", ChapterUrl, true);
    }

    [Fact]
    public async Task GetChapterImageUrls_HarvestsEveryPageOfTheVirtualizedStripInReadingOrder()
    {
        var comix = CreateComix(CreateMockClient(""), CreateReaderBrowser(new FakeLongStripReader(pageCount: 30)));

        string[] urls = await comix.GetChapterImageUrls(ComixChapter(comix));

        Assert.Equal(Enumerable.Range(0, 30).Select(FakeLongStripReader.ImageUrl), urls);
    }

    [Fact]
    public async Task GetChapterImageUrls_ThrowsWhenTheReaderShowsItsEmptyState()
    {
        // A captcha-blocked chapter API renders the reader shell as .rpage-empty.
        var comix = CreateComix(CreateMockClient(""), CreateReaderBrowser(new FakeLongStripReader(30, rendered: false, blocked: true)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => comix.GetChapterImageUrls(ComixChapter(comix)));
        Assert.Contains("blocked", ex.Message);
    }

    [Fact]
    public async Task GetChapterImageUrls_ThrowsWhenNoLongStripReaderRendered()
    {
        // e.g. a paged reader mode or drifted markup: harvesting would silently save a partial chapter.
        var comix = CreateComix(CreateMockClient(""), CreateReaderBrowser(new FakeLongStripReader(30, rendered: false)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => comix.GetChapterImageUrls(ComixChapter(comix)));
        Assert.Contains("long-strip", ex.Message);
    }

    [Fact]
    public async Task GetChapterImageUrls_ThrowsOnScrambledCanvasPages()
    {
        // Scrambled pages are drawn to a <canvas> instead of an <img>; not supported yet, so the chapter must
        // fail rather than download with pages missing.
        var comix = CreateComix(CreateMockClient(""), CreateReaderBrowser(new FakeLongStripReader(30, canvasAt: 17)));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => comix.GetChapterImageUrls(ComixChapter(comix)));
        Assert.Contains("scrambled", ex.Message);
    }

    [Fact]
    public async Task DownloadImage_SendsNoReferer()
    {
        // The image hosts' hotlink protection is inverted: a comix.to Referer gets 403, none gets the image.
        var client = CreateMockClient("bytes");
        var comix = CreateComix(client);

        await comix.DownloadImage(FakeLongStripReader.ImageUrl(1), CancellationToken.None);

        client.Verify(c => c.MakeRequest(FakeLongStripReader.ImageUrl(1), It.IsAny<RequestType>(), null, It.IsAny<CancellationToken?>()));
    }
}

public class ComixRealBrowserTests : IDisposable
{
    private readonly API.Tests.Unit.HttpRequesters.LocalTestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [API.Tests.Unit.HttpRequesters.RequiresChromiumFact]
    public async Task GetChapterImageUrls_IgnoresNonPageImagesInsideTheReader()
    {
        string url = _pages.Page("reader-extra-images.html", "", """
            <main class="rpage-main--long-strip">
              <div class="rpage-page"><img class="rpage-page__img" src="https://img.invalid/page.webp"></div>
              <aside><img src="https://img.invalid/advert.gif"></aside>
            </main>
            """);
        KenkuSettings settings = new();
        await using ChromiumHeadlessBrowser browser = new(settings);
        Comix comix = new(settings, new Mock<IHttpRequester>().Object, browser);
        Series series = new("S", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
        Chapter chapter = new(series, "1", null);

        string[] urls = await comix.GetChapterImageUrls(new SourceId<Chapter>(chapter, comix, "1", url, true));

        Assert.Equal(["https://img.invalid/page.webp"], urls);
    }

    [API.Tests.Unit.HttpRequesters.RequiresChromiumFact]
    public async Task GetChapterImageUrls_WaitsForTheReaderAfterAnInitialEmptyState()
    {
        string url = _pages.Page("reader-loading.html", """
            setTimeout(() => {
              document.querySelector('.rpage-empty').remove();
              document.body.insertAdjacentHTML('beforeend',
                '<main class="rpage-main--long-strip"><img class="rpage-page__img" src="https://img.invalid/page.webp"></main>');
            }, 1200);
            """, "<div class=\"rpage-empty\">Loading chapter</div>");
        KenkuSettings settings = new();
        await using ChromiumHeadlessBrowser browser = new(settings);
        Comix comix = new(settings, new Mock<IHttpRequester>().Object, browser);
        Series series = new("S", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
        Chapter chapter = new(series, "1", null);

        string[] urls = await comix.GetChapterImageUrls(new SourceId<Chapter>(chapter, comix, "1", url, true));

        Assert.Equal(["https://img.invalid/page.webp"], urls);
    }

    [API.Tests.Unit.HttpRequesters.RequiresChromiumFact]
    public async Task GetChapterImageUrls_HarvestsAVirtualizedStripInARealBrowser()
    {
        string url = _pages.VirtualizedStrip(pageCount: 8);
        var settings = new KenkuSettings();
        await using var browser = new ChromiumHeadlessBrowser(settings);
        var comix = new Comix(settings, new Mock<IHttpRequester>().Object, browser);
        var series = new Series("S", "", "", SeriesReleaseStatus.Continuing, [], [], [], []);
        var chapter = new Chapter(series, "1", null);

        string[] urls = await comix.GetChapterImageUrls(new SourceId<Chapter>(chapter, comix, "1", url, true));

        Assert.Equal(Enumerable.Range(0, 8).Select(i => $"https://img.invalid/p{i}.webp"), urls);
    }
}
