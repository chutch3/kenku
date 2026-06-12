using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace API.Discovery;

/// <summary>
/// AniList's public GraphQL endpoint — manga shelves, no API key. The edge client owns the HTTP;
/// everything above it depends on <see cref="IAniListClient"/>.
/// </summary>
public class AniListClient(HttpClient http) : IAniListClient
{
    private const string Endpoint = "https://graphql.anilist.co";
    private const string ListQuery =
        "query($perPage:Int,$sort:[MediaSort],$genre:String,$startDateGreater:FuzzyDateInt)" +
        "{Page(perPage:$perPage){media(type:MANGA,sort:$sort,isAdult:false,genre:$genre,startDate_greater:$startDateGreater)" +
        "{title{romaji english}coverImage{large}siteUrl description(asHtml:false)}}}";

    public async Task<List<DiscoveryEntry>> GetMangaListAsync(AniListShelf shelf, int limit, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.PostAsync(Endpoint,
            new StringContent(BuildRequestBody(shelf, limit), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        return ParseMedia(await response.Content.ReadAsStringAsync(ct));
    }

    public static string BuildRequestBody(AniListShelf shelf, int limit)
    {
        // Unset filters are left out of the variables entirely: GraphQL treats an unprovided
        // nullable variable as "argument not supplied", while an explicit null would filter on it.
        var variables = new Dictionary<string, object> { ["perPage"] = limit, ["sort"] = new[] { shelf.Sort } };
        if (shelf.Genre is not null) variables["genre"] = shelf.Genre;
        // FuzzyDateInt is YYYYMMDD; YYYY0000 matches anything from that year on.
        if (shelf.MinStartYear is { } year) variables["startDateGreater"] = year * 10000;
        return JsonSerializer.Serialize(new { query = ListQuery, variables });
    }

    public static List<DiscoveryEntry> ParseMedia(string json)
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
