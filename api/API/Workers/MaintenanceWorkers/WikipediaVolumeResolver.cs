using API.Services.Interfaces;
using System.Text.RegularExpressions;
using API.Schema.SeriesContext;
using API.Services;
using Newtonsoft.Json.Linq;

namespace API.Workers.MaintenanceWorkers;

/// <summary>
/// Resolves volumes from a Wikipedia "List of &lt;series&gt; chapters" article. These pages use the
/// <c>{{Graphic novel list}}</c> / <c>{{Numbered list}}</c> templates, which enumerate every chapter
/// under its volume — the only complete, machine-readable source for many ongoing series whose later
/// volumes aren't tagged on MangaDex. Parsing the wikitext (via the MediaWiki API) is far more robust
/// than scraping rendered HTML. Any failure (no such page, unexpected markup) yields an empty map.
/// </summary>
public class WikipediaVolumeResolver(HttpClient httpClient) : IVolumeResolver
{
    private readonly HttpClient _httpClient = httpClient.WithKenkuUserAgent();

    public string SourceName => "Wikipedia";
    public MetadataConfidence Confidence => MetadataConfidence.Exact;

    private static readonly Regex VolumeNumberRegex = new(@"VolumeNumber\s*=\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex StartRegex = new(@"start\s*=\s*(\d+)", RegexOptions.Compiled);

    public async Task<IReadOnlyDictionary<string, int>> ResolveAsync(
        Series manga, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
    {
        try
        {
            string pageTitle = $"List of {manga.Name} chapters";
            string url = $"https://en.wikipedia.org/w/api.php?action=parse&page={Uri.EscapeDataString(pageTitle)}" +
                         "&prop=wikitext&format=json&redirects=1";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, int>();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (json["parse"]?["wikitext"]?["*"]?.ToString() is not { Length: > 0 } wikitext)
                return new Dictionary<string, int>();

            return ParseVolumeMap(wikitext);
        }
        catch
        {
            // The chapter-list page may not exist or may use unexpected markup — fail soft.
            return new Dictionary<string, int>();
        }
    }

    /// <summary>Parses chapter→volume assignments from chapter-list wikitext. Internal for testing.</summary>
    internal static IReadOnlyDictionary<string, int> ParseVolumeMap(string wikitext)
    {
        var map = new Dictionary<string, int>();

        foreach (var block in ExtractTemplateBlocks(wikitext, "{{Graphic novel list"))
        {
            if (block.StartsWith("{{Graphic novel list/header"))
                continue;

            var volMatch = VolumeNumberRegex.Match(block);
            if (!volMatch.Success || !int.TryParse(volMatch.Groups[1].Value, out int volume))
                continue;

            // A volume's chapters are split across one or more {{Numbered list|start=K|...}} templates.
            foreach (var numberedList in ExtractTemplateBlocks(block, "{{Numbered list"))
            {
                var startMatch = StartRegex.Match(numberedList);
                if (!startMatch.Success || !int.TryParse(startMatch.Groups[1].Value, out int start))
                    continue;

                int count = CountTopLevelItems(numberedList);
                for (int ch = start; ch < start + count; ch++)
                    map[ch.ToString()] = volume;
            }
        }

        return map;
    }

    /// <summary>Extracts each balanced <c>{{…}}</c> block that begins with <paramref name="marker"/>.</summary>
    private static List<string> ExtractTemplateBlocks(string text, string marker)
    {
        var blocks = new List<string>();
        int i = 0;
        while ((i = text.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            int depth = 0, j = i;
            while (j < text.Length)
            {
                if (j + 1 < text.Length && text[j] == '{' && text[j + 1] == '}') { j++; continue; }
                if (j + 1 < text.Length && text[j] == '{' && text[j + 1] == '{') { depth++; j += 2; continue; }
                if (j + 1 < text.Length && text[j] == '}' && text[j + 1] == '}')
                {
                    depth--; j += 2;
                    if (depth == 0) break;
                    continue;
                }
                j++;
            }
            blocks.Add(text[i..j]);
            i = j;
        }
        return blocks;
    }

    /// <summary>
    /// Counts the list items in a <c>{{Numbered list|start=K|item|item|…}}</c> by counting top-level
    /// <c>|</c> separators (depth 1) and subtracting the <c>start=</c> parameter. Pipes inside nested
    /// templates or <c>[[links|with pipes]]</c> sit at a deeper level and are ignored.
    /// </summary>
    private static int CountTopLevelItems(string numberedList)
    {
        int depth = 0, topLevelPipes = 0;
        for (int k = 0; k < numberedList.Length; k++)
        {
            if (k + 1 < numberedList.Length &&
                ((numberedList[k] == '{' && numberedList[k + 1] == '{') ||
                 (numberedList[k] == '[' && numberedList[k + 1] == '[')))
            {
                depth++; k++; continue;
            }
            if (k + 1 < numberedList.Length &&
                ((numberedList[k] == '}' && numberedList[k + 1] == '}') ||
                 (numberedList[k] == ']' && numberedList[k + 1] == ']')))
            {
                depth--; k++; continue;
            }
            if (numberedList[k] == '|' && depth == 1)
                topLevelPipes++;
        }
        // The first top-level pipe is the start= parameter; the rest are list items.
        return Math.Max(0, topLevelPipes - 1);
    }
}
