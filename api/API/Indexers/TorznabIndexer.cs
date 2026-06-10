using API.Indexers.Interfaces;
using System.Xml.Linq;
using log4net;

namespace API.Indexers;

/// <summary>
/// A single Torznab/Newznab indexer. Works for any Torznab endpoint — a manually-added tracker,
/// a Jackett feed, or one of the per-indexer endpoints Prowlarr exposes at
/// <c>{prowlarr}/{indexerId}/api</c>. Knows nothing about Prowlarr; that's the whole point.
/// </summary>
public class TorznabIndexer(HttpClient http, string name, string baseUrl, string apiKey, int[] categories,
    IndexerCooldown? cooldown = null) : IIndexer
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(TorznabIndexer));
    private static readonly XNamespace Torznab = "http://torznab.com/schemas/2015/feed";

    public string Name { get; } = name;

    public async Task<IndexerSearchResult[]> Search(IndexerQuery query, CancellationToken ct)
    {
        // A rate-limited indexer is skipped until its cooldown elapses — searching it again just
        // burns the daily quota and gets another 429. (Was: a 429 per chapter, 51 in an hour.)
        if (cooldown?.CooldownUntil(Name) is { } until)
        {
            Log.DebugFormat("Torznab indexer '{0}' is rate-limited until {1:u}; skipping search.", Name, until);
            return [];
        }

        try
        {
            string url = BuildUrl(query);
            using HttpResponseMessage response = await http.GetAsync(url, ct);
            if ((int)response.StatusCode == 429)
            {
                TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
                cooldown?.RecordRateLimited(Name, retryAfter is { Ticks: > 0 } ra ? ra : null);
                Log.WarnFormat("Torznab indexer '{0}' returned HTTP 429; cooling down{1}.", Name,
                    retryAfter is { Ticks: > 0 } r ? $" for {r.TotalSeconds:0}s" : "");
                return [];
            }
            if (!response.IsSuccessStatusCode)
            {
                Log.WarnFormat("Torznab indexer '{0}' returned HTTP {1}", Name, (int)response.StatusCode);
                return [];
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            XDocument doc = XDocument.Parse(body);

            return doc.Descendants("item")
                .Select(ParseItem)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.ErrorFormat("Torznab indexer '{0}' search threw: {1}", Name, ex);
            return [];
        }
    }

    private string BuildUrl(IndexerQuery query)
    {
        string term = string.IsNullOrEmpty(query.IssueNumber)
            ? query.SeriesTitle
            : $"{query.SeriesTitle} {query.IssueNumber}";

        // The indexer's own categories win: when Prowlarr syncs an indexer (or a user adds one by
        // hand) it carries the comic-category mapping for *that specific* endpoint, which is the
        // authoritative filter. The query categories are only a fallback for indexers configured
        // without any categories of their own — forcing a global guess (e.g. 8000) over a real
        // per-indexer mapping silently filters every comic out on trackers that tag them elsewhere.
        int[] cats = categories is { Length: > 0 } ? categories : query.Categories ?? [];

        var parts = new List<string>
        {
            "t=search",
            $"apikey={Uri.EscapeDataString(apiKey)}",
            $"q={Uri.EscapeDataString(term)}"
        };
        if (cats.Length > 0)
            parts.Add($"cat={string.Join(',', cats)}");

        string sep = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{sep}{string.Join('&', parts)}";
    }

    private IndexerSearchResult? ParseItem(XElement item)
    {
        string title = (string?)item.Element("title") ?? "";
        if (string.IsNullOrEmpty(title)) return null;

        // Prefer an explicit <link> when it points at a torrent/magnet; otherwise the enclosure url.
        string? link = (string?)item.Element("link");
        string? enclosure = item.Element("enclosure")?.Attribute("url")?.Value;
        string downloadUrl = PickDownloadUrl(link, enclosure);
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        long size = ParseLong((string?)item.Element("size"))
                    ?? ParseLong(TorznabAttr(item, "size"))
                    ?? 0;
        int seeders = (int)(ParseLong(TorznabAttr(item, "seeders")) ?? 0);

        return new IndexerSearchResult(title, downloadUrl, size, seeders, Name);
    }

    private static string PickDownloadUrl(string? link, string? enclosure)
    {
        bool LinkIsDownloadable(string? s) =>
            !string.IsNullOrEmpty(s) &&
            (s.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
             s.Contains(".torrent", StringComparison.OrdinalIgnoreCase) ||
             s.Contains("/download", StringComparison.OrdinalIgnoreCase));

        if (LinkIsDownloadable(link)) return link!;
        if (!string.IsNullOrEmpty(enclosure)) return enclosure!;
        return link ?? "";
    }

    private static string? TorznabAttr(XElement item, string name) =>
        item.Elements(Torznab + "attr")
            .FirstOrDefault(a => (string?)a.Attribute("name") == name)
            ?.Attribute("value")?.Value;

    private static long? ParseLong(string? s) => long.TryParse(s, out long v) ? v : null;
}
