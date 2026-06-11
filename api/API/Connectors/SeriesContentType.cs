namespace API.Connectors;

/// <summary>
/// Resolves a series' content type from its sources: comic when every linked source serves comics;
/// a mixed or unknown source set stays manga so manga machinery keeps working. Mirrors the UI rule
/// in web/website/app/composables/useSeriesKind.ts — keep the two in sync, including the
/// case-insensitive name match.
/// </summary>
public static class SeriesContentType
{
    public static bool IsComic(Schema.SeriesContext.Series series, IEnumerable<SeriesSource> connectors) =>
        series.SourceIds.Count > 0 && series.SourceIds.All(id =>
            connectors.FirstOrDefault(c => c.Name.Equals(id.MangaConnectorName, StringComparison.OrdinalIgnoreCase))
                ?.ContentType == ContentType.Comic);
}
