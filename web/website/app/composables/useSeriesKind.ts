import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type Connector = components['schemas']['SeriesSource'];

export type SeriesKind = 'manga' | 'comic';

/** A series is a comic when every source it has declares comic content — what the source serves,
 * not how it acquires it. Comics have no MangaDex/AniList notion of volume mapping. Mixed or
 * unknown sources behave as manga. Mirrors the backend rule in API/Connectors/SeriesContentType.cs —
 * keep the two in sync, including the case-insensitive name match. */
export function seriesKind(series: AnySeries, connectors?: Connector[] | null): SeriesKind {
    const types = (series.sourceIds ?? []).map(
        (s) => connectors?.find((c) => c.name?.toLowerCase() === s.mangaConnectorName?.toLowerCase())?.contentType
    );
    return types.length > 0 && types.every((t) => t === 'Comic') ? 'comic' : 'manga';
}

export type MediaFilter = 'all' | SeriesKind;

/** Library-side filter: 'all' is the escape that keeps everything (so owned content is never hidden by
 * default); a specific kind keeps only series of that kind. */
export function matchesMediaFilter(series: AnySeries, filter: MediaFilter, connectors?: Connector[] | null): boolean {
    return filter === 'all' || seriesKind(series, connectors) === filter;
}
