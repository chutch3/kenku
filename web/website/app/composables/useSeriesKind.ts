import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type Connector = components['schemas']['SeriesSource'];

export type SeriesKind = 'manga' | 'comic';

/** A series is a comic when every source it has declares comic content — what the source serves,
 * not how it acquires it. Comics have no MangaDex/AniList notion of volume mapping. Mixed or
 * unknown sources behave as manga. */
export function seriesKind(series: AnySeries, connectors?: Connector[] | null): SeriesKind {
    const types = (series.sourceIds ?? []).map((s) => connectors?.find((c) => c.name === s.mangaConnectorName)?.contentType);
    return types.length > 0 && types.every((t) => t === 'Comic') ? 'comic' : 'manga';
}
