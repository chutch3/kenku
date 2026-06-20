namespace API.Schema.SeriesContext;

/// <summary>
/// Provenance + precedence of a series' cover, lowest to highest. A present cover is only replaced by an
/// equal-or-higher-ranked source, so cover writes are deterministic instead of last-writer-wins:
/// a metadata provider (MAL/Metron) backfill never clobbers a connector cover, and a connector sync
/// never clobbers a cover the user explicitly chose.
/// </summary>
public enum CoverSource
{
    None,
    Provider,
    Connector,
    User
}
