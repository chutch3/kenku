using API.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using API.Schema.SeriesContext;
using Newtonsoft.Json.Linq;

namespace API.Workers.MaintenanceWorkers;

public class MangaDexVolumeResolver(HttpClient httpClient) : IMangaDexVolumeResolver
{
    private readonly HttpClient _httpClient = httpClient.WithKenkuUserAgent();

    public async Task<Dictionary<string, int>> GetChapterToVolumeMapAsync(Series manga, CancellationToken cancellationToken = default)
    {
        string? mangadexUuid = null;

        // 1. Prefer a confirmed or auto-matched ExternalId from MetadataSource
        if (manga.MetadataSource?.ExternalId is { Length: > 0 } externalId &&
            manga.MetadataSource.Status is MetadataSourceStatus.Confirmed or MetadataSourceStatus.AutoMatched)
        {
            mangadexUuid = externalId;
        }
        else
        {
            // 2. Fall back to a MangaDex connector id, if the series was sourced from MangaDex directly.
            // We intentionally do NOT fall back to a blind title search: an unverified top-hit can link the
            // wrong series. Matching now happens up front in auto-match (by AniList id, then scored title),
            // which sets a trusted ExternalId; series it can't confidently link are left for manual linking.
            var mdConnector = manga.SourceIds.FirstOrDefault(c => c.MangaConnectorName.Equals("MangaDex", StringComparison.OrdinalIgnoreCase));
            if (mdConnector != null)
                mangadexUuid = mdConnector.IdOnConnectorSite;
        }

        if (string.IsNullOrEmpty(mangadexUuid))
            return [];

        // Prefer English volume tags — they match the chapter numbering an English library actually
        // has — then fill any gaps from the all-languages aggregate. Many series (e.g. Dandadan) tag
        // almost nothing in English but are fully tagged in other languages.
        var map = await FetchVolumeMapAsync(mangadexUuid, "?translatedLanguage[]=en", cancellationToken);
        var allLanguages = await FetchVolumeMapAsync(mangadexUuid, "", cancellationToken);
        foreach (var (chapter, volume) in allLanguages)
            map.TryAdd(chapter, volume);

        return map;
    }

    /// <summary>
    /// Fetches one aggregate (optionally language-filtered) and reduces it to a chapter→volume map.
    /// Where the same chapter is tagged under multiple volumes, the smallest (earliest) volume wins so
    /// the result is deterministic regardless of JSON ordering.
    /// </summary>
    private async Task<Dictionary<string, int>> FetchVolumeMapAsync(string mangadexUuid, string querySuffix, CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, int>();

        var response = await _httpClient.GetAsync($"https://api.mangadex.org/manga/{mangadexUuid}/aggregate{querySuffix}", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return map;

        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (json["volumes"] is not JObject volumesObj)
            return map;

        foreach (var volProp in volumesObj.Properties())
        {
            if (volProp.Value is not JObject volEntry) continue;

            string volStr = volEntry["volume"]?.ToString() ?? "";
            if (!int.TryParse(volStr, out int volNum)) continue;

            if (volEntry["chapters"] is not JObject chaptersObj) continue;

            foreach (var chapProp in chaptersObj.Properties())
            {
                if (chapProp.Value is not JObject chapEntry) continue;
                string chapStr = chapEntry["chapter"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(chapStr)) continue;

                string key = NormalizeChapterNumber(chapStr);
                if (!map.TryGetValue(key, out int existing) || volNum < existing)
                    map[key] = volNum;
            }
        }

        return map;
    }

    private static string NormalizeChapterNumber(string chapter)
    {
        var parts = chapter.Split('.');
        var normalized = parts.Select(p => int.TryParse(p, out int n) ? n.ToString() : p);
        return string.Join('.', normalized);
    }
}
