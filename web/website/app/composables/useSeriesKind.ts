import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type Connector = components['schemas']['SeriesSource'];

export type SeriesKind = 'manga' | 'comic';

/** A series is a comic when every source it has is indexer/torrent-backed — those arrive as whole
 * archives via a download client and have no MangaDex/AniList notion of volume mapping. Mixed or
 * unknown sources behave as manga. */
export function seriesKind(series: AnySeries, connectors?: Connector[] | null): SeriesKind {
    const kinds = (series.sourceIds ?? []).map((s) => connectors?.find((c) => c.name === s.mangaConnectorName)?.kind);
    return kinds.length > 0 && kinds.every((k) => k === 'Torrent') ? 'comic' : 'manga';
}
