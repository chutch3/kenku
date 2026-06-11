using System.Xml.Linq;

namespace API.Discovery;

/// <summary>Reddit's public Atom feeds. Reddit rate-limits aggressively (429), so callers cache and degrade.</summary>
public class RedditFeedClient(HttpClient http) : IRedditFeedClient
{
    public async Task<List<DiscoveryEntry>> GetHotAsync(string subreddit, int limit, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"https://www.reddit.com/r/{subreddit}/hot.rss?limit={limit}", ct);
        response.EnsureSuccessStatusCode();
        return ParseFeed(await response.Content.ReadAsStringAsync(ct), subreddit);
    }

    public static List<DiscoveryEntry> ParseFeed(string atomXml, string subreddit)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace media = "http://search.yahoo.com/mrss/";
        XDocument doc = XDocument.Parse(atomXml);
        var entries = new List<DiscoveryEntry>();
        foreach (XElement entry in doc.Root!.Elements(atom + "entry"))
        {
            // AutoModerator entries are the pinned megathreads, not discovery material.
            string author = entry.Element(atom + "author")?.Element(atom + "name")?.Value ?? "";
            if (author.EndsWith("/AutoModerator", StringComparison.OrdinalIgnoreCase))
                continue;
            entries.Add(new DiscoveryEntry(
                entry.Element(atom + "title")?.Value ?? "",
                entry.Element(media + "thumbnail")?.Attribute("url")?.Value ?? "",
                entry.Element(atom + "link")?.Attribute("href")?.Value ?? "",
                $"r/{subreddit}",
                null));
        }
        return entries;
    }
}
