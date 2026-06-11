using API.HttpRequesters.Interfaces;
using System.Text.RegularExpressions;
using System.Web;
using API.Acquirers;
using API.Acquirers.Interfaces;
using API.HttpRequesters;
using API.Indexers;
using API.Schema.SeriesContext;
using HtmlAgilityPack;

namespace API.Connectors;

/// <summary>
/// GetComics.org as a direct-download comic source: the WordPress search is scraped, posts are
/// collapsed into series by parsed title (the <see cref="IndexerBackedSeriesSource"/> model), and
/// each post becomes a chapter whose WebsiteUrl is the post page. Kind = DirectArchive routes
/// downloads through the archive path, with the post resolved to its archive URL lazily at
/// download time (<see cref="IArchiveUrlResolver"/>) so syncs never re-scrape every post.
/// </summary>
public class GetComics : SeriesSource, IArchiveUrlResolver
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

    private static readonly Regex PixeldrainShareUrl = new(@"^https?://pixeldrain\.com/u/([A-Za-z0-9]+)", RegexOptions.Compiled);

    /// <summary>
    /// Resolves a post page to a fetchable archive URL. The Main Server button ("Download Now", a
    /// redirect to the file) wins; Pixeldrain and Mediafire get per-host resolvers; everything else
    /// (Mega, Terabox, WeTransfer, …) can't be automated and is parked for manual handling with the
    /// hosts named. Posts bundling several Download Now buttons (one per issue) are parked too —
    /// fetching just the first would silently drop the rest.
    /// </summary>
    public async Task<ArchiveResolution> ResolveArchiveUrl(SourceId<Chapter> chapter, CancellationToken ct)
    {
        string postUrl = chapter.WebsiteUrl!;
        using HttpResponseMessage response = await downloadClient.MakeRequest(postUrl, RequestType.Default, cancellationToken: ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GetComics post request failed: HTTP {(int)response.StatusCode} for {postUrl}");

        HtmlDocument doc = new();
        doc.LoadHtml(await response.Content.ReadAsStringAsync(ct));
        if (doc.DocumentNode.SelectSingleNode("//h1[contains(@class, 'post-title')]") is null)
            throw new HttpRequestException($"GetComics page {postUrl} does not look like a post — the selectors may have drifted or an error page was served.");

        (string Title, string Href)[] buttons = (doc.DocumentNode.SelectNodes(
                    "//div[contains(concat(' ', normalize-space(@class), ' '), ' aio-button-center ')]//a[@title]")
                ?? Enumerable.Empty<HtmlNode>())
            .Select(a => (Title: a.GetAttributeValue("title", "").Trim(), Href: a.GetAttributeValue("href", "")))
            .Where(b => b.Title.Length > 0 && b.Href.Length > 0)
            .ToArray();

        (string Title, string Href)[] mainServer = buttons
            .Where(b => b.Title.Equals("Download Now", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (mainServer.Length == 1)
            return new ArchiveResolution.Resolved(mainServer[0].Href);
        if (mainServer.Length > 1)
            return new ArchiveResolution.Manual($"the post bundles {mainServer.Length} separate downloads — download manually");

        foreach ((string title, string href) in buttons)
        {
            if (title.Equals("Pixeldrain", StringComparison.OrdinalIgnoreCase))
                return await ResolvePixeldrain(href, ct);
            if (title.Equals("Mediafire", StringComparison.OrdinalIgnoreCase))
                return await ResolveMediafire(href, ct);
        }

        string[] hosts = buttons
            .Where(b => !b.Title.Equals("Read Online", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ArchiveResolution.Manual(hosts.Length == 0
            ? "the post has no download links — download manually"
            : $"only available via {string.Join(", ", hosts)} — download manually");
    }

    /// <summary>The button links to the share page (often via a getcomics redirect); the file itself
    /// is served by Pixeldrain's API, so the share id is rewritten into the direct-file URL.</summary>
    private async Task<ArchiveResolution> ResolvePixeldrain(string href, CancellationToken ct)
    {
        Match match = PixeldrainShareUrl.Match(href);
        if (!match.Success)
        {
            using HttpResponseMessage response = await downloadClient.MakeRequest(href, RequestType.Default, cancellationToken: ct);
            match = PixeldrainShareUrl.Match(response.RequestMessage?.RequestUri?.ToString() ?? "");
        }
        return match.Success
            ? new ArchiveResolution.Resolved($"https://pixeldrain.com/api/file/{match.Groups[1].Value}?download")
            : new ArchiveResolution.Manual("couldn't resolve the Pixeldrain link — download manually");
    }

    private async Task<ArchiveResolution> ResolveMediafire(string href, CancellationToken ct)
    {
        using HttpResponseMessage response = await downloadClient.MakeRequest(href, RequestType.Default, cancellationToken: ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Mediafire page request failed: HTTP {(int)response.StatusCode} for {href}");
        HtmlDocument doc = new();
        doc.LoadHtml(await response.Content.ReadAsStringAsync(ct));
        string fileUrl = doc.DocumentNode.SelectSingleNode("//a[@id='downloadButton']")?.GetAttributeValue("href", "") ?? "";
        return fileUrl.Length > 0
            ? new ArchiveResolution.Resolved(fileUrl)
            : new ArchiveResolution.Manual("couldn't resolve the Mediafire link — download manually");
    }

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
