import { describe, it, expect } from 'vitest';
import { seriesKind } from '~/composables/useSeriesKind';
import type { components } from '#open-fetch-schemas/api';

type MinimalSeries = components['schemas']['MinimalSeries'];
type Connector = components['schemas']['SeriesSource'];

const connectors = [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'Indexers', name: 'Indexers', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'Torrent', contentType: 'Comic' },
    { key: 'GetComics', name: 'GetComics', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
    { key: 'ComicHubFree', name: 'ComicHubFree', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Comic' },
] as Connector[];

function withSources(...names: string[]): MinimalSeries {
    return {
        key: 's1',
        name: 'X',
        description: '',
        releaseStatus: 'Continuing',
        sourceIds: names.map((n, i) => ({
            key: `sid${i}`,
            mangaConnectorName: n,
            objId: 's1',
            idOnConnectorSite: `id${i}`,
            websiteUrl: null,
            useForDownload: true,
        })),
        fileLibraryId: 'lib1',
        originalLanguage: 'en',
        coverUrl: '',
    } as MinimalSeries;
}

describe('seriesKind', () => {
    it('is comic when every source is indexer/torrent-backed', () => {
        expect(seriesKind(withSources('Indexers'), connectors)).toBe('comic');
    });

    it('is comic when every source is direct-archive (GetComics)', () => {
        expect(seriesKind(withSources('GetComics'), connectors)).toBe('comic');
    });

    it('is comic for mixed torrent + direct-archive sources', () => {
        expect(seriesKind(withSources('Indexers', 'GetComics'), connectors)).toBe('comic');
    });

    it('is comic for a page-reader comic source, despite its manga-style ImageList kind', () => {
        expect(seriesKind(withSources('ComicHubFree'), connectors)).toBe('comic');
        expect(seriesKind(withSources('ComicHubFree', 'GetComics'), connectors)).toBe('comic');
    });

    it('is manga for scrape-site sources', () => {
        expect(seriesKind(withSources('WeebCentral'), connectors)).toBe('manga');
    });

    it('mixed sources behave as manga (volume mapping stays useful)', () => {
        expect(seriesKind(withSources('Indexers', 'WeebCentral'), connectors)).toBe('manga');
    });

    it('matches connector names case-insensitively, like the backend lookups', () => {
        const series = withSources('getcomics');
        expect(seriesKind(series, connectors)).toBe('comic');
    });

    it('defaults to manga without sources or connector data', () => {
        expect(seriesKind(withSources(), connectors)).toBe('manga');
        expect(seriesKind(withSources('Indexers'), null)).toBe('manga');
    });
});
