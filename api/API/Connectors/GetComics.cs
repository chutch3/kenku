using API.HttpRequesters.Interfaces;
using System.Web;
using API.Acquirers;
using API.HttpRequesters;
using API.Indexers;
using API.Schema.SeriesContext;
using HtmlAgilityPack;

namespace API.Connectors;

/// <summary>
/// GetComics.org as a direct-download comic source: the WordPress search is scraped, posts are
/// collapsed into series by parsed title (the <see cref="IndexerBackedSeriesSource"/> model), and
/// each post becomes a chapter whose WebsiteUrl is the post page. Kind = DirectArchive routes
/// downloads through the archive path instead of page scraping.
/// </summary>
public class GetComics : SeriesSource
{
    // Posts per page is 12; five pages bounds a chapter sync at 60 posts while staying polite.
    private const int MaxSearchPages = 5;

    public GetComics(KenkuSettings settings, IHttpRequester downloadClient)
        : base("GetComics", ["en"], ["getcomics.org"], "https://getcomics.org/share/uploads/2020/04/cropped-GetComics-Favicon.png", settings)
    {
        this.downloadClient = downloadClient;
    }

    public override AcquisitionKind Kind => AcquisitionKind.DirectArchive;

    private sealed record Post(string Title, string Url, string CoverUrl);

    public override async Task<(Series, SourceId<Series>)[]> SearchManga(string mangaSearchName)
    {
        Post[] posts = await FetchSearchPage(SearchUrl(mangaSearchName, 1));

        var list = new List<(Series, SourceId<Series>)>();
        foreach (var group in posts
                     .Select(p => (Post: p, Parsed: ReleaseTitleParser.Parse(p.Title)))
                     .Where(p => !string.IsNullOrWhiteSpace(p.Parsed.SeriesTitle))
                     .GroupBy(p => p.Parsed.SeriesTitle, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(BuildSeries(group.First().Parsed.SeriesTitle,
                group.Select(p => p.Parsed.Year).FirstOrDefault(y => y.HasValue),
                group.Select(p => p.Post.CoverUrl).FirstOrDefault(c => !string.IsNullOrEmpty(c)) ?? ""));
        }
        Log.InfoFormat("Search '{0}' yielded {1} series from {2} posts.", mangaSearchName, list.Count, posts.Length);
        return list.ToArray();
    }

    public override async Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url)
    {
        using HttpResponseMessage response = await downloadClient.MakeRequest(url, RequestType.MangaInfo);
        if (!response.IsSuccessStatusCode)
        {
            Log.ErrorFormat("Failed to retrieve post page {0}: HTTP {1}", url, (int)response.StatusCode);
            return null;
        }

        HtmlDocument doc = new();
        doc.LoadHtml(await response.Content.ReadAsStringAsync());

        HtmlNode? titleNode = doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'post-title')]");
        if (titleNode is null)
            return null;

        ParsedRelease parsed = ReleaseTitleParser.Parse(HtmlEntity.DeEntitize(titleNode.InnerText).Trim());
        string coverUrl = doc.DocumentNode
            .SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "") ?? "";
        return BuildSeries(parsed.SeriesTitle, parsed.Year, coverUrl);
    }

    public override async Task<(Series, SourceId<Series>)?> GetMangaFromId(string mangaIdOnSite)
    {
        // The id is the collapsed series title; re-searching reproduces the same collapse (and its
        // cover — building the series bare here would lose it at add time).
        (Series, SourceId<Series>)[] results = await SearchManga(mangaIdOnSite);
        return results.Cast<(Series, SourceId<Series>)?>().FirstOrDefault(r =>
            string.Equals(r!.Value.Item1.Name, mangaIdOnSite, StringComparison.OrdinalIgnoreCase));
    }

    public override async Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> mangaId, string? language = null)
    {
        string seriesTitle = mangaId.Obj.Name;
        var byIssue = new Dictionary<string, (Chapter, SourceId<Chapter>)>();
        for (int page = 1; page <= MaxSearchPages; page++)
        {
            Post[]? posts = await FetchSearchPage(SearchUrl(seriesTitle, page), missingPageIsEnd: page > 1);
            if (posts is null || posts.Length == 0)
                break;

            foreach (Post post in posts)
            {
                ParsedRelease parsed = ReleaseTitleParser.Parse(post.Title);
                if (!string.Equals(parsed.SeriesTitle, seriesTitle, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (parsed.IssueNumber is null)
                {
                    Log.WarnFormat("Skipping post without a parseable issue number: {0}", post.Title);
                    continue;
                }
                if (byIssue.ContainsKey(parsed.IssueNumber))
                    continue;

                var chapter = new Chapter(mangaId.Obj, parsed.IssueNumber, null, post.Title);
                var chId = new SourceId<Chapter>(chapter, this, parsed.IssueNumber, post.Url, mangaId.UseForDownload);
                chapter.SourceIds.Add(chId);
                byIssue[parsed.IssueNumber] = (chapter, chId);
            }
        }
        Log.InfoFormat("Found {0} chapters for {1}", byIssue.Count, seriesTitle);
        return byIssue.Values.ToArray();
    }

    // GetComics posts are finished archives; there are no page images to enumerate or download.
    internal override Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId)
        => throw new NotSupportedException("GetComics (direct-archive) sources do not expose page images.");

    public override Task<Stream?> DownloadImage(string imageUrl, CancellationToken ct)
        => throw new NotSupportedException("GetComics (direct-archive) sources do not download images.");

    private static string SearchUrl(string query, int page) =>
        page == 1
            ? $"https://getcomics.org/?s={HttpUtility.UrlEncode(query)}"
            : $"https://getcomics.org/page/{page}/?s={HttpUtility.UrlEncode(query)}";

    /// <summary>
    /// Fetches one search-results page and extracts its posts. Distinguishes "legitimately empty"
    /// (the post-list section is present with zero articles) from a selector miss or error page,
    /// which throws so a sync surfaces the breakage instead of silently emptying the series.
    /// </summary>
    private async Task<Post[]?> FetchSearchPage(string url, bool missingPageIsEnd = false)
    {
        using HttpResponseMessage response = await downloadClient.MakeRequest(url, RequestType.Default);
        if (missingPageIsEnd && response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // WordPress 404s pages past the last one — the normal end of paging.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GetComics search request failed: HTTP {(int)response.StatusCode} for {url}");

        HtmlDocument doc = new();
        doc.LoadHtml(await response.Content.ReadAsStringAsync());

        HtmlNode? postList = doc.DocumentNode.SelectSingleNode(
            "//section[contains(concat(' ', normalize-space(@class), ' '), ' post-list ')]");
        if (postList is null)
            throw new HttpRequestException($"GetComics page {url} has no post-list section — the selectors may have drifted or an error page was served.");

        HtmlNodeCollection? articles = postList.SelectNodes(".//article");
        if (articles is null)
            return [];

        var posts = new List<Post>();
        foreach (HtmlNode article in articles)
        {
            HtmlNode? link = article.SelectSingleNode(".//h1[contains(@class, 'post-title')]/a");
            if (link is null)
                throw new HttpRequestException($"GetComics post on {url} has no title link — the selectors may have drifted.");
            string coverUrl = article
                .SelectSingleNode(".//div[contains(@class, 'post-header-image')]//img")
                ?.GetAttributeValue("src", "") ?? "";
            posts.Add(new Post(
                HtmlEntity.DeEntitize(link.InnerText).Trim(),
                link.GetAttributeValue("href", ""),
                coverUrl));
        }
        return posts.ToArray();
    }

    private (Series, SourceId<Series>) BuildSeries(string title, int? year, string coverUrl)
    {
        var series = new Series(
            title, "", coverUrl, SeriesReleaseStatus.Continuing,
            [], [], [], [],
            year: year.HasValue ? (uint)year.Value : null,
            originalLanguage: "en");
        var id = new SourceId<Series>(series, this, title, null);
        series.SourceIds.Add(id);
        return (series, id);
    }
}
