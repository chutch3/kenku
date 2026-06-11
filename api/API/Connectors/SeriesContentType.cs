namespace API.Connectors;

/// <summary>
/// Resolves a series' content type from its sources, mirroring the UI rule: comic when every linked
/// source serves comics; a mixed or unknown source set stays manga so manga machinery keeps working.
/// </summary>
public static class SeriesContentType
{
    public static bool IsComic(Schema.SeriesContext.Series series, IEnumerable<SeriesSource> connectors) =>
        series.SourceIds.Count > 0 && series.SourceIds.All(id =>
            connectors.FirstOrDefault(c => c.Name.Equals(id.MangaConnectorName, StringComparison.OrdinalIgnoreCase))
                ?.ContentType == ContentType.Comic);
}
