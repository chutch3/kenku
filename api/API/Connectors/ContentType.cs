namespace API.Connectors;

/// <summary>
/// What a source serves — independent of HOW it delivers it (<see cref="Acquirers.AcquisitionKind"/>).
/// A page-reader comic site is still a comic source; the UI diverges the comic experience on this.
/// </summary>
public enum ContentType
{
    Manga,
    Comic,
}
