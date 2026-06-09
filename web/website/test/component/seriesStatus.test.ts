import { describe, it, expect } from 'vitest';
import { seriesTrackState } from '~/composables/useSeriesStatus';
import type { components } from '#open-fetch-schemas/api';

type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

function series(overrides: Partial<MinimalSeries> = {}): MinimalSeries {
    return {
        key: 's1',
        name: 'Saga',
        description: '',
        releaseStatus: 'Continuing',
        sourceIds: [{ key: 'sid1', mangaConnectorName: 'Src', objId: 's1', idOnConnectorSite: 'x', websiteUrl: null, useForDownload: true }],
        fileLibraryId: 'lib1',
        originalLanguage: 'en',
        coverUrl: '',
        ...overrides,
    } as MinimalSeries;
}

function rollup(overrides: Partial<SeriesRollup> = {}): SeriesRollup {
    return {
        mangaId: 's1',
        wantedChapters: 10,
        downloadedChapters: 10,
        queuedJobs: 0,
        runningJobs: 0,
        needsAttentionJobs: 0,
        lastError: null,
        lastSyncAt: null,
        lastSyncChapterCount: null,
        ...overrides,
    } as SeriesRollup;
}

describe('seriesTrackState', () => {
    it('is untracked without a library, whatever the rollup says', () => {
        expect(seriesTrackState(series({ fileLibraryId: null }), rollup())).toBe('untracked');
    });

    it('is paused when no source is enabled', () => {
        const s = series();
        s.sourceIds![0]!.useForDownload = false;
        expect(seriesTrackState(s, rollup())).toBe('paused');
    });

    it('is attention when a job for the series needs attention', () => {
        expect(seriesTrackState(series(), rollup({ needsAttentionJobs: 1 }))).toBe('attention');
    });

    it('is downloading while jobs are queued or running', () => {
        expect(seriesTrackState(series(), rollup({ queuedJobs: 2 }))).toBe('downloading');
        expect(seriesTrackState(series(), rollup({ runningJobs: 1 }))).toBe('downloading');
    });

    it('is downloading while wanted chapters are missing, even with an idle queue', () => {
        expect(seriesTrackState(series(), rollup({ wantedChapters: 10, downloadedChapters: 4 }))).toBe('downloading');
    });

    it('is up to date when everything wanted is on disk and nothing is in flight', () => {
        expect(seriesTrackState(series(), rollup())).toBe('upToDate');
    });

    it('falls back to downloading when no rollup is available yet', () => {
        expect(seriesTrackState(series())).toBe('downloading');
    });
});
