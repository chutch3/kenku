using API.HttpRequesters.Interfaces;
﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using API.Acquirers;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using HtmlAgilityPack;
using log4net;
using Microsoft.EntityFrameworkCore;

namespace API.Connectors;

[PrimaryKey("Name")]
[Table("MangaConnector")] // Existing DB table; a follow-up hand-crafted migration is needed to rename to "SeriesSource" (see TECHNICAL_DEBT.md).
public abstract class SeriesSource(string name, string[] supportedLanguages, string[] baseUris, string iconUrl, KenkuSettings settings)
{
    [NotMapped] internal IHttpRequester downloadClient { get; init; } = null!;
    [NotMapped] protected ILog Log { get; init; } = LogManager.GetLogger(name);
    [StringLength(32)] public string Name { get; init; } = name;
    [StringLength(8)] public string[] SupportedLanguages { get; init; } = supportedLanguages;
    [StringLength(2048)] public string IconUrl { get; init; } = iconUrl;
    [StringLength(256)] public string[] BaseUris { get; init; } = baseUris;
    public bool Enabled { get; internal set; } = true;
    protected KenkuSettings Settings => settings;

    // Known external metadata trackers a series page may link out to, matched on the shape of an *entry*
    // URL (not just the domain) so a generic footer/home link isn't mistaken for the series' identity.
    // Their stable ids cross-reference to MangaDex's own `links` (e.g. AniList), enabling identifier
    // matching instead of a fuzzy title guess.
    private static readonly (string Provider, Regex EntryUrl)[] TrackerProviders =
    {
        ("AniList", new Regex(@"^https?://(www\.)?anilist\.co/manga/\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("MyAnimeList", new Regex(@"^https?://(www\.)?myanimelist\.net/manga/\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("MangaUpdates", new Regex(@"^https?://(www\.)?mangaupdates\.com/series[/.]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("Anime Planet", new Regex(@"^https?://(www\.)?anime-planet\.com/manga/", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    };

    /// <summary>
    /// Collects external metadata-tracker links (AniList, MangaUpdates, ...) from a parsed series page.
    /// Connectors that surface these should keep them so the series can be matched to a metadata source
    /// by identifier rather than by title.
    /// </summary>
    protected static List<Link> ParseExternalLinks(HtmlDocument doc)
    {
        HtmlNodeCollection? anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
            return [];

        var links = new List<Link>();
        var seen = new HashSet<string>();
        foreach (HtmlNode anchor in anchors)
        {
            string href = anchor.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href))
                continue;
            foreach ((string provider, Regex entryUrl) in TrackerProviders)
            {
                if (entryUrl.IsMatch(href) && seen.Add(href))
                {
                    links.Add(new Link(provider, href));
                    break;
                }
            }
        }
        return links;
    }

    /// <summary>How this source delivers chapters. Drives dispatch to the matching IChapterAcquirer.</summary>
    [NotMapped] public abstract AcquisitionKind Kind { get; }

    /// <summary>What this source serves. Comic sources opt in; the historical scrapers are manga.</summary>
    [NotMapped] public virtual ContentType ContentType => ContentType.Manga;

    public abstract Task<(Series, SourceId<Series>)[]> SearchManga(string mangaSearchName);

    public abstract Task<(Series, SourceId<Series>)?> GetMangaFromUrl(string url);

    public abstract Task<(Series, SourceId<Series>)?> GetMangaFromId(string mangaIdOnSite);

    public abstract Task<(Chapter, SourceId<Chapter>)[]> GetChapters(SourceId<Series> mangaId,
        string? language = null);

    internal abstract Task<string[]> GetChapterImageUrls(SourceId<Chapter> chapterId);

    public bool UrlMatchesConnector(string url) => BaseUris.Any(baseUri => Regex.IsMatch(url, "https?://" + baseUri + "/.*"));

    internal Task<string?> SaveCoverImageToCache(SourceId<Series> mangaId, int retries = 3) =>
        Services.CoverImageCache.SaveAsync(settings, downloadClient, mangaId, retries);

    public virtual async Task<Stream?> DownloadImage(string imageUrl, CancellationToken ct)
    {
        HttpResponseMessage requestResult = await downloadClient.MakeRequest(imageUrl, RequestType.MangaImage, cancellationToken: ct);
        return requestResult.IsSuccessStatusCode ? await requestResult.Content.ReadAsStreamAsync(ct) : null;
    }
}
