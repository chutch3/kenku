using API.HttpRequesters.Interfaces;
using System.Text.RegularExpressions;
using System.Web;
using API.Acquirers;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using HtmlAgilityPack;

namespace API.Connectors;

/// <summary>
/// ComicHubFree as a comic page-reader source: issues are chapters, each issue's pages are scraped
/// from the site's "/all" reading view and packaged into a .cbz by the ImageList path — the same
/// model as the manga scrapers, pointed at western comics. Complements GetComics with per-issue
/// granularity for back-catalogue runs that only exist there as packs.
/// </summary>
public class ComicHubFree : SeriesSource
{
    // The issue list paginates at 50 rows; 20 pages bounds a sync at 1000 issues.
    private const int MaxListPages = 20;

    private static readonly Regex SeriesUrlRx = new(@"https?://(?:www\.)?comichubfree\.com/comic/(?<slug>[^/?#]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IssueNumberRx = new(@"issue-0*(\d+(?:\.\d+)?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PageTitleRx = new(@"^Read\s+(?<name>.+?)\s+(?:Comics\s+)?Online for Free$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReleasedRx = new(@"Released:\s*(\d{4})", RegexOptions.Compiled);

    public ComicHubFree(KenkuSettings settings, IHttpRequester downloadClient)
        : base("ComicHubFree", ["en"], ["comichubfree.com"], "https://comichubfree.com/images/site/logo.png", settings)
    {
        this.downloadClient = downloadClient;
    }

    public override AcquisitionKind Kind => AcquisitionKind.ImageList;

    public override ContentType ContentType => ContentType.Comic;

    public override async Task<(Series, SourceId<Series>)[]> SearchManga(string mangaSearchName)
    {
        string requestUrl = $"https://comichubfree.com/search-comic?key={HttpUtility.UrlEncode(mangaSearchName)}";
        HtmlDocument doc = await FetchDocument(requestUrl);

        HtmlNodeCollection? cards = doc.DocumentNode.SelectNodes("//div[contains(@class, 'cartoon-box')]");
        if (cards is null)
            throw new HttpRequestException($"ComicHubFree search page {requestUrl} has no result cards — the selectors may have drifted or an error page was served.");

        var results = new List<(Series, SourceId<Series>)>();
        foreach (HtmlNode card in cards)
        {
            // The no-results page renders one card whose heading has no link; that is a legitimate
            // empty result, not drift.
            HtmlNode? link = card.SelectSingleNode(".//h3/a");
            if (link is null)
                continue;
            Match urlMatch = SeriesUrlRx.Match(link.GetAttributeValue("href", ""));
            if (!urlMatch.Success)
                continue;

            string name = HtmlEntity.DeEntitize(link.InnerText).Trim();
            string coverUrl = LazyImageUrl(card.SelectSingleNode(".//a[contains(@class, 'image')]//img"));
            Match released = ReleasedRx.Match(HtmlEntity.DeEntitize(card.InnerText));
            uint? year = released.Success ? uint.Parse(released.Groups[1].Value) : null;
            results.Add(BuildSeries(name, urlMatch.Groups["slug"].Value, coverUrl, year));
        }
        Log.InfoFormat("Search '{0}' yielded {1} results.", mangaSearchName, results.Count);
        return results.ToArray();
    }

    public override async Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url)
    {
        Match match = SeriesUrlRx.Match(url);
        return match.Success ? await GetMangaFromId(match.Groups["slug"].Value) : null;
    }

    public override async Task<(Series, SourceId<Series>)?> GetMangaFromId(string mangaIdOnSite)
    {
        HtmlDocument doc = await FetchDocument(SeriesUrl(mangaIdOnSite), RequestType.MangaInfo);

        HtmlNode? titleNode = doc.DocumentNode.SelectSingleNode("//span[contains(@class, 'title-1')]");
        if (titleNode is null)
            return null;
        string heading = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
        Match title = PageTitleRx.Match(heading);
        string name = title.Success ? title.Groups["name"].Value : heading;

        string coverUrl = doc.DocumentNode
            .SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "") ?? "";
        return BuildSeries(name, mangaIdOnSite, coverUrl, null);
    }

    public override async Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> mangaId, string? language = null)
    {
        string slug = mangaId.IdOnConnectorSite;
        var chapters = new List<(Chapter, SourceId<Chapter>)>();
        var seen = new HashSet<string>();
        for (int page = 1; page <= MaxListPages; page++)
        {
            string url = page == 1 ? SeriesUrl(slug) : $"{SeriesUrl(slug)}?page={page}";
            HtmlDocument doc = await FetchDocument(url);

            HtmlNode? episodeList = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'episode-list')]");
            if (episodeList is null)
                throw new HttpRequestException($"ComicHubFree series page {url} has no episode list — the selectors may have drifted or an error page was served.");

            foreach (HtmlNode link in episodeList.SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                string href = link.GetAttributeValue("href", "");
                Match issue = IssueNumberRx.Match(href);
                if (!issue.Success)
                {
                    Log.WarnFormat("Skipping issue without a parseable number: {0}", href);
                    continue;
                }
                string number = issue.Groups[1].Value;
                if (!seen.Add(number))
                    continue;

                var chapter = new Chapter(mangaId.Obj, number, null, HtmlEntity.DeEntitize(link.InnerText).Trim());
                var chId = new SourceId<Chapter>(chapter, this, $"issue-{number}", href, mangaId.UseForDownload);
                chapter.SourceIds.Add(chId);
                chapters.Add((chapter, chId));
            }

            if (doc.DocumentNode.SelectSingleNode($"//ul[contains(@class, 'pagination')]//a[contains(@href, 'page={page + 1}')]") is null)
                break;
        }
        Log.InfoFormat("Found {0} chapters for {1}", chapters.Count, mangaId.Obj.Name);
        return chapters.OrderBy(c => c.Item1, new Chapter.ChapterComparer()).ToArray();
    }

    internal override async Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId)
    {
        if (chapterId.WebsiteUrl is null)
        {
            Log.ErrorFormat("Chapter {0} has no issue URL.", chapterId);
            return [];
        }

        // The per-page reading view shows one image at a time; the "/all" view lists every page.
        string url = $"{chapterId.WebsiteUrl.TrimEnd('/')}/all";
        HtmlDocument doc = await FetchDocument(url);

        string[] imageUrls = (doc.DocumentNode.SelectNodes("//img[contains(@class, 'chapter_img')]") ?? Enumerable.Empty<HtmlNode>())
            .Select(LazyImageUrl)
            .Where(u => u.Length > 0)
            .ToArray();
        if (imageUrls.Length == 0)
            Log.WarnFormat("No page images found on {0}", url);
        return imageUrls;
    }

    /// <summary>Lazyloaded images keep a base64 placeholder in src and the real URL in data-src.</summary>
    private static string LazyImageUrl(HtmlNode? img)
    {
        if (img is null)
            return "";
        string dataSrc = img.GetAttributeValue("data-src", "");
        if (dataSrc.Length > 0)
            return dataSrc;
        string src = img.GetAttributeValue("src", "");
        return src.StartsWith("data:") ? "" : src;
    }

    private static string SeriesUrl(string slug) => $"https://comichubfree.com/comic/{slug}";

    private async Task<HtmlDocument> FetchDocument(string url, RequestType requestType = RequestType.Default)
    {
        using HttpResponseMessage response = await downloadClient.MakeRequest(url, requestType);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"ComicHubFree request failed: HTTP {(int)response.StatusCode} for {url}");
        HtmlDocument doc = new();
        doc.LoadHtml(await response.Content.ReadAsStringAsync());
        return doc;
    }

    private (Series, SourceId<Series>) BuildSeries(string name, string slug, string coverUrl, uint? year)
    {
        var series = new Series(
            name, "", coverUrl, SeriesReleaseStatus.Continuing,
            [], [], [], [],
            year: year,
            originalLanguage: "en");
        var id = new SourceId<Series>(series, this, slug, SeriesUrl(slug));
        series.SourceIds.Add(id);
        return (series, id);
    }
}
