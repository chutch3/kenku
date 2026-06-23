namespace API.Controllers.Requests;

/// <summary>A manually-added Torznab/Newznab indexer. <see cref="ApiKey"/> may be blank on an edit to
/// mean "keep the stored key" — it is redacted on the read path, so the UI never round-trips it.</summary>
public record SetManualIndexerRecord(
    string Name,
    string Url,
    string? ApiKey,
    int[]? Categories);
