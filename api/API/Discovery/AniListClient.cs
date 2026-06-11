using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace API.Discovery;

/// <summary>
/// AniList's public GraphQL endpoint — trending manga, no API key. The edge client owns the HTTP;
/// everything above it depends on <see cref="IAniListClient"/>.
/// </summary>
public class AniListClient(HttpClient http) : IAniListClient
{
    private const string Endpoint = "https://graphql.anilist.co";
    private const string TrendingQuery =
        "query($perPage:Int){Page(perPage:$perPage){media(type:MANGA,sort:TRENDING_DESC,isAdult:false)" +
        "{title{romaji english}coverImage{large}siteUrl description(asHtml:false)}}}";

    public async Task<List<DiscoveryEntry>> GetTrendingMangaAsync(int limit, CancellationToken ct)
    {
        string body = JsonSerializer.Serialize(new { query = TrendingQuery, variables = new { perPage = limit } });
        using HttpResponseMessage response = await http.PostAsync(Endpoint,
            new StringContent(body, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        return ParseTrending(await response.Content.ReadAsStringAsync(ct));
    }

    public static List<DiscoveryEntry> ParseTrending(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement media = doc.RootElement.GetProperty("data").GetProperty("Page").GetProperty("media");
        var entries = new List<DiscoveryEntry>();
        foreach (JsonElement m in media.EnumerateArray())
        {
            JsonElement title = m.GetProperty("title");
            string? english = title.GetProperty("english").GetString();
            string name = string.IsNullOrEmpty(english) ? title.GetProperty("romaji").GetString() ?? "" : english;
            string? description = m.GetProperty("description").ValueKind == JsonValueKind.String
                ? m.GetProperty("description").GetString() : null;
            entries.Add(new DiscoveryEntry(
                name,
                m.GetProperty("coverImage").GetProperty("large").GetString() ?? "",
                m.GetProperty("siteUrl").GetString() ?? "",
                "AniList",
                description is null ? null : StripHtml(description)));
        }
        return entries;
    }

    private static readonly Regex HtmlTagRx = new("<[^>]+>", RegexOptions.Compiled);
    private static string StripHtml(string html) =>
        Regex.Replace(HtmlTagRx.Replace(html, " "), @"\s+", " ").Trim();
}
