import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

export type SeriesSort = 'name-asc' | 'name-desc' | 'updated' | 'attention';

/** Sort a library list. Returns a new array; ties always fall back to name so the order is stable.
 * `updated` reads the rollup's lastSyncAt; `attention` floats series with a failing job to the top. */
export function sortSeries<T extends AnySeries>(list: T[], rollups: Record<string, SeriesRollup>, sort: SeriesSort): T[] {
    const byName = (a: T, b: T) => a.name.localeCompare(b.name);
    const syncedAt = (s: T) => {
        const at = rollups[s.key]?.lastSyncAt;
        return at ? Date.parse(at) : 0;
    };
    const needsAttention = (s: T) => (seriesTrackState(s, rollups[s.key]) === 'attention' ? 0 : 1);

    const arr = [...list];
    switch (sort) {
        case 'name-desc':
            return arr.sort((a, b) => byName(b, a));
        case 'updated':
            return arr.sort((a, b) => syncedAt(b) - syncedAt(a) || byName(a, b));
        case 'attention':
            return arr.sort((a, b) => needsAttention(a) - needsAttention(b) || byName(a, b));
        case 'name-asc':
        default:
            return arr.sort(byName);
    }
}
