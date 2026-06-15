import { describe, it, expect } from 'vitest';
import { sortSeries } from '~/utils/sortSeries';
import type { components } from '#open-fetch-schemas/api';

type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

function series(key: string, name: string, tracked = true): MinimalSeries {
    return {
        key, name, description: '', releaseStatus: 'Continuing',
        sourceIds: [{ key: 's', mangaConnectorName: 'Src', objId: key, idOnConnectorSite: 'x', websiteUrl: null, useForDownload: true }],
        fileLibraryId: tracked ? 'lib1' : null, originalLanguage: 'en', coverUrl: '',
    } as MinimalSeries;
}
function rollup(mangaId: string, overrides: Partial<SeriesRollup> = {}): SeriesRollup {
    return {
        mangaId, wantedChapters: 10, downloadedChapters: 10, queuedJobs: 0, runningJobs: 0, needsAttentionJobs: 0,
        lastError: null, lastSyncAt: null, lastSyncChapterCount: null, ...overrides,
    } as SeriesRollup;
}

const a = series('a', 'Akira');
const b = series('b', 'Berserk');
const c = series('c', 'Chainsaw Man');
const list = [c, a, b];

describe('sortSeries', () => {
    it('sorts by name ascending and descending', () => {
        expect(sortSeries(list, {}, 'name-asc').map((s) => s.name)).toEqual(['Akira', 'Berserk', 'Chainsaw Man']);
        expect(sortSeries(list, {}, 'name-desc').map((s) => s.name)).toEqual(['Chainsaw Man', 'Berserk', 'Akira']);
    });

    it('sorts most-recently-updated first, names breaking ties', () => {
        const rollups = {
            a: rollup('a', { lastSyncAt: '2026-06-10T00:00:00Z' }),
            c: rollup('c', { lastSyncAt: '2026-06-14T00:00:00Z' }),
            // b has no sync time → sinks to the bottom
        };
        expect(sortSeries(list, rollups, 'updated').map((s) => s.name)).toEqual(['Chainsaw Man', 'Akira', 'Berserk']);
    });

    it('floats series that need attention to the top', () => {
        const rollups = { b: rollup('b', { needsAttentionJobs: 1 }) };
        expect(sortSeries(list, rollups, 'attention').map((s) => s.name)).toEqual(['Berserk', 'Akira', 'Chainsaw Man']);
    });

    it('does not mutate the input array', () => {
        const input = [c, a, b];
        sortSeries(input, {}, 'name-asc');
        expect(input.map((s) => s.name)).toEqual(['Chainsaw Man', 'Akira', 'Berserk']);
    });
});
