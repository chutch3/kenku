using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using API.Acquirers;
using API.HttpRequesters;
using API.HttpRequesters.Interfaces;
using API.Schema.SeriesContext;
using HtmlAgilityPack;

namespace API.Connectors;

public class Comix : SeriesSource
{
    private const string BaseUrl = "https://comix.to";

    // A title page or any page beneath it (e.g. a chapter): /title/{hid}-{slug}[/...]
    private static readonly Regex TitleUrl = new(@"^https?://(?:www\.)?comix\.to/title/(?<hid>[a-z0-9]+)(?:-[^/]*)?(?:/|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FooterTotal = new(@"of\s+(?<total>[\d,]+)\s+items", RegexOptions.Compiled);
    private static readonly Regex ChapterLabel = new(@"^Ch\.\s*(?<number>\d+(?:\.\d+)?)$", RegexOptions.Compiled);
    private static readonly Regex VolumeLabel = new(@"^Vol\.\s*(?<volume>\d+)$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> TrackerLinkProviders = new()
    {
        ["al"] = "AniList",
        ["mal"] = "MyAnimeList",
        ["mu"] = "MangaUpdates",
    };

    private readonly IHeadlessBrowser _browser;

    public Comix(KenkuSettings settings, IHttpRequester downloadClient, IHeadlessBrowser browser)
        : base("Comix", ["en"], ["comix.to"], "https://comix.to/favicon.ico", settings)
    {
        _browser = browser;
        this.downloadClient = downloadClient;
    }

    public override AcquisitionKind Kind => AcquisitionKind.ImageList;

    // The chapter list and reader load their data from an API comix.to gates behind a human check (403
    // captcha_required), so they render in the browser session named after this source, which carries an
    // imported session when one is configured.

    // Results come from the signed API, so the browse page is rendered and its first page of result cards
    // read directly, without a request per result. A blocked API or drifted markup renders neither the
    // cards nor the empty state, which is a failure rather than "no results".
    public override async Task<(Series, SourceId<Series>)[]> SearchSeries(string mangaSearchName)
    {
        string url = $"{BaseUrl}/browse?keyword={HttpUtility.UrlEncode(mangaSearchName)}&sort=relevance%3Adesc&content_rating=safe%2Csuggestive%2Cpornographic%2Cerotica";
        HtmlDocument doc = new();
        doc.LoadHtml(await _browser.RenderHtmlAsync(url, waitForSelector: ".lrow, .list-empty"));

        HtmlNodeCollection? cards = doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' lrow ')]");
        if (cards is null)
        {
            if (doc.DocumentNode.SelectSingleNode("//*[contains(@class, 'list-empty')]") is not null)
                return [];
            throw new HttpRequestException($"Comix search page {url} rendered neither results nor its empty state — the data API may be blocked or the selectors may have drifted.");
        }

        (Series, SourceId<Series>)[] results = cards.Select(ParseSearchCard).OfType<(Series, SourceId<Series>)>().ToArray();
        Log.InfoFormat("Search '{0}' yielded {1} results.", mangaSearchName, results.Length);
        return results;
    }

    private (Series, SourceId<Series>)? ParseSearchCard(HtmlNode card)
    {
        HtmlNode? link = card.SelectSingleNode(".//a[contains(@class, 'lrow__title-link')]");
        Match match = TitleUrl.Match(BaseUrl + (link?.GetAttributeValue("href", "") ?? ""));
        if (link is null || !match.Success)
            return null;

        string poster = card.SelectSingleNode(".//a[contains(@class, 'lrow__poster')]//img")?.GetAttributeValue("src", "") ?? "";
        string statusClass = card.SelectSingleNode(".//*[contains(@class, 'lrow__status')]")?.GetAttributeValue("class", "") ?? "";
        uint? year = uint.TryParse(TextOf(card, "lrow__year"), out uint y) && y > 0 ? y : null;

        Series series = new(TextOf(card, "lrow__title"), TextOf(card, "lrow__desc"),
            string.IsNullOrEmpty(poster) ? "" : BaseUrl + poster,
            ParseStatus(statusClass.Split("lrow__status--").ElementAtOrDefault(1)?.Trim()),
            [], [], [], [], year: year);
        SourceId<Series> sourceId = new(series, this, match.Groups["hid"].Value, BaseUrl + link.GetAttributeValue("href", ""));
        series.SourceIds.Add(sourceId);
        return (series, sourceId);
    }

    public override async Task<(Series, SourceId<Series>)?> GetSeriesFromUrl(string url)
    {
        Match match = TitleUrl.Match(url);
        return match.Success ? await GetSeriesFromId(match.Groups["hid"].Value) : null;
    }

    // The title page is server-rendered, but it is still fetched through the browser: comix.to's Cloudflare
    // can block plain HTTP clients from the homelab VPN. The shared browser handles challenge recovery.
    public override async Task<(Series, SourceId<Series>)?> GetSeriesFromId(string mangaIdOnSite)
    {
        HtmlDocument doc = new();
        doc.LoadHtml(await _browser.RenderHtmlAsync($"{BaseUrl}/title/{mangaIdOnSite}", waitForSelector: "#initial-data"));
        string? initialData = doc.DocumentNode.SelectSingleNode("//script[@id='initial-data']")?.InnerText;
        if (string.IsNullOrWhiteSpace(initialData))
        {
            Log.ErrorFormat("Title page for {0} has no initial-data", mangaIdOnSite);
            return null;
        }

        using JsonDocument json = JsonDocument.Parse(initialData);
        JsonElement? detail = json.RootElement.GetProperty("queries").EnumerateObject()
            .Where(q => q.Name.StartsWith("[\"manga\",\"detail\""))
            .Select(q => (JsonElement?)q.Value)
            .FirstOrDefault();
        if (detail is null)
        {
            Log.ErrorFormat("Title page for {0} has no manga detail", mangaIdOnSite);
            return null;
        }

        return ParseSeries(detail.Value);
    }

    private (Series, SourceId<Series>) ParseSeries(JsonElement detail)
    {
        string hid = detail.GetProperty("hid").GetString()!;

        SeriesReleaseStatus status = ParseStatus(StringOrNull(detail, "status"));

        string? poster = detail.TryGetProperty("poster", out JsonElement p) ? StringOrNull(p, "large") : null;

        List<Author> authors = Titles(detail, "authors").Concat(Titles(detail, "artists"))
            .Distinct().Select(a => new Author(a)).ToList();
        List<SeriesTag> tags = Titles(detail, "genres").Select(t => new SeriesTag(t)).ToList();
        List<AltTitle> altTitles = detail.TryGetProperty("altTitles", out JsonElement alts)
            ? alts.EnumerateArray().Select(a => new AltTitle("", a.GetString()!)).ToList()
            : [];
        List<Link> links = detail.TryGetProperty("links", out JsonElement l) && l.ValueKind == JsonValueKind.Object
            ? l.EnumerateObject()
                .Where(kv => TrackerLinkProviders.ContainsKey(kv.Name) && kv.Value.ValueKind == JsonValueKind.String)
                .Select(kv => new Link(TrackerLinkProviders[kv.Name], kv.Value.GetString()!))
                .ToList()
            : [];
        uint? year = detail.TryGetProperty("year", out JsonElement y) && y.TryGetUInt32(out uint yv) && yv > 0 ? yv : null;

        Series series = new(detail.GetProperty("title").GetString()!,
            StringOrNull(detail, "synopsis") ?? "",
            poster is null ? "" : BaseUrl + poster,
            status, authors, tags, links, altTitles,
            year: year, originalLanguage: StringOrNull(detail, "originalLanguage"));

        string websiteUrl = BaseUrl + (StringOrNull(detail, "url") ?? $"/title/{hid}");
        SourceId<Series> sourceId = new(series, this, hid, websiteUrl);
        series.SourceIds.Add(sourceId);
        return (series, sourceId);
    }

    private static SeriesReleaseStatus ParseStatus(string? status) => status switch
    {
        "releasing" => SeriesReleaseStatus.Continuing,
        "finished" => SeriesReleaseStatus.Completed,
        "on_hiatus" => SeriesReleaseStatus.OnHiatus,
        "discontinued" => SeriesReleaseStatus.Cancelled,
        _ => SeriesReleaseStatus.Unreleased
    };

    private static string? StringOrNull(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static IEnumerable<string> Titles(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(e => StringOrNull(e, "title")).OfType<string>()
            : [];

    // The chapter list is rendered from the signed API and paginated by URL, newest first, with every
    // scan-group upload as its own row (SeriesChapterSyncService collapses uploads per chapter). Paging
    // stops at the first page holding only uploads this series already tracks, so a re-sync costs one
    // render; only the first sync walks every page.
    public override async Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> seriesId, string? language = null)
    {
        string seriesUrl = seriesId.WebsiteUrl ?? $"{BaseUrl}/title/{seriesId.IdOnConnectorSite}";
        HashSet<string> known = (seriesId.Obj.Chapters ?? [])
            .SelectMany(c => c.SourceIds ?? [])
            .Where(id => id.SeriesSourceName == Name)
            .Select(id => id.IdOnConnectorSite)
            .ToHashSet();

        List<(Chapter, SourceId<Chapter>)> chapters = [];
        int pageCount = 1;
        for (int page = 1; page <= pageCount; page++)
        {
            string url = page == 1 ? seriesUrl : $"{seriesUrl}?page={page}";
            HtmlDocument doc = new();
            doc.LoadHtml(await _browser.RenderHtmlAsync(url, waitForSelector: ".mchap-row", session: Name));

            HtmlNodeCollection? rows = doc.DocumentNode.SelectNodes("//div[contains(concat(' ', normalize-space(@class), ' '), ' mchap-row ')]");
            if (rows is null)
                throw new HttpRequestException($"Comix chapter list did not render on {url} — Comix may need verification (its data API is blocked until the human check is passed), or the selectors may have drifted.");

            if (page == 1)
                pageCount = PageCount(doc, rows.Count);

            List<(Chapter, SourceId<Chapter>)> parsed = rows.Select(r => ParseChapterRow(r, seriesId.Obj)).OfType<(Chapter, SourceId<Chapter>)>().ToList();
            chapters.AddRange(parsed);
            if (parsed.Count > 0 && parsed.All(c => known.Contains(c.Item2.IdOnConnectorSite)))
                break;
        }

        Log.InfoFormat("Found {0} chapter uploads for {1}", chapters.Count, seriesId.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    private static int PageCount(HtmlDocument doc, int pageSize)
    {
        Match match = FooterTotal.Match(TextOf(doc.DocumentNode, "mchap-foot__hint"));
        return match.Success && int.TryParse(match.Groups["total"].Value.Replace(",", ""), out int total)
            ? Math.Max(1, (int)Math.Ceiling(total / (double)pageSize))
            : 1;
    }

    private (Chapter, SourceId<Chapter>)? ParseChapterRow(HtmlNode row, Series series)
    {
        HtmlNode? primary = row.SelectSingleNode(".//a[contains(@class, 'mchap-row__primary')]");
        string href = primary?.GetAttributeValue("href", "") ?? "";
        string chapterId = new string(href.Split('/').Last().TakeWhile(char.IsDigit).ToArray());
        string label = TextOf(row, "mchap-row__ch");
        Match number = ChapterLabel.Match(label);
        if (primary is null || chapterId.Length == 0 || !number.Success)
        {
            Log.WarnFormat("Skipping chapter row without a numeric chapter: '{0}' ({1})", label, href);
            return null;
        }

        int? volume = primary.SelectNodes(".//span")?
            .Select(span => VolumeLabel.Match(span.InnerText.Trim()))
            .Where(m => m.Success)
            .Select(m => (int?)int.Parse(m.Groups["volume"].Value))
            .FirstOrDefault();
        string? title = TextOf(row, "mchap-row__title") is { Length: > 0 } t ? t : null;
        string? group = TextOf(row, "mchap-row__group") is { Length: > 0 } g && g != "Unknown group" ? g : null;

        Chapter chapter = new(series, number.Groups["number"].Value, volume, title);
        SourceId<Chapter> sourceId = new(chapter, this, chapterId, BaseUrl + href, scanGroup: group);
        return (chapter, sourceId);
    }

    private static string TextOf(HtmlNode scope, string cssClass) =>
        HtmlEntity.DeEntitize(scope.SelectSingleNode($".//*[contains(@class, '{cssClass}')]")?.InnerText ?? "").Trim();

    private const string Reader = "main.rpage-main--long-strip";
    private const int ScrollStepPx = 800, ScrollSettleMs = 450, MaxScrollSteps = 400, IdleStepsAtEnd = 3;

    // The reader is a virtualized long strip that only mounts the images near the viewport, so pages are
    // collected while stepping down the strip until it is at the end and nothing new has appeared. Both the
    // strip and the document are scrolled, so it works whichever of them is the scrolling element. Image
    // bytes are then fetched over plain HTTP by DownloadImage, with no Referer: the image hosts reject a
    // comix.to one.
    internal override async Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId)
    {
        string url = chapterId.WebsiteUrl ?? throw new ArgumentException($"Chapter {chapterId.Obj} has no reader URL.");
        return await _browser.DrivePageAsync(url, async page =>
        {
            // The initial page can contain .rpage-empty while its chapter request is still loading.
            // Wait for the real reader; only interpret an empty state after the bounded wait expires.
            await page.WaitForSelectorAsync(Reader);
            if ((await page.QueryAttributesAsync(Reader, "class")).Count == 0)
            {
                throw (await page.QueryAttributesAsync(".rpage-empty", "class")).Count > 0
                    ? new HttpRequestException($"Comix reader on {url} rendered no pages — the chapter API may be blocked until Comix is verified, or the chapter is unavailable.")
                    : new HttpRequestException($"Comix reader on {url} did not render the long-strip reader — it may be in another reading mode or the selectors may have drifted.");
            }

            List<string> urls = [];
            HashSet<string> seen = [];
            int idle = 0;
            for (int step = 0; step < MaxScrollSteps && idle < IdleStepsAtEnd; step++)
            {
                if ((await page.QueryAttributesAsync($"{Reader} canvas", "class")).Count > 0)
                    throw new HttpRequestException($"Comix chapter {url} has scrambled (canvas) pages, which are not supported yet.");

                int before = urls.Count;
                // The main container can also contain recommendations/advert images. Only the site's
                // page-image elements belong in the archive (or in its missing-page count).
                foreach (string src in await page.QueryAttributesAsync($"{Reader} img.rpage-page__img", "src"))
                {
                    string absolute = src.StartsWith('/') ? BaseUrl + src : src;
                    if (absolute.StartsWith("http") && seen.Add(absolute))
                        urls.Add(absolute);
                }

                bool stripAtEnd = await page.ScrollByAsync(Reader, ScrollStepPx, 0);
                bool documentAtEnd = await page.ScrollByAsync(null, ScrollStepPx, ScrollSettleMs);
                idle = urls.Count == before && stripAtEnd && documentAtEnd ? idle + 1 : 0;
            }

            if (urls.Count == 0)
                throw new HttpRequestException($"Comix reader on {url} yielded no page images.");
            if (idle < IdleStepsAtEnd)
                throw new HttpRequestException($"Comix reader on {url} did not reach the end of the chapter within {MaxScrollSteps} scroll steps.");
            Log.InfoFormat("Found {0} images for chapter {1}", urls.Count, chapterId.Obj);
            return urls.ToArray();
        }, session: Name);
    }
}
