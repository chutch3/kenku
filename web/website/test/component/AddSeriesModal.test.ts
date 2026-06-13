import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint, mockNuxtImport } from '@nuxt/test-utils/runtime';
import { createError, getQuery } from 'h3';
import AddSeriesModal from '~/components/AddSeriesModal.vue';

const { navigateToMock } = vi.hoisted(() => ({ navigateToMock: vi.fn() }));
mockNuxtImport('navigateTo', () => navigateToMock);

const series = {
    key: 's1',
    name: 'Fire Punch',
    description: 'A frozen world.',
    releaseStatus: 'Continuing',
    sourceIds: [
        { key: 'sid1', mangaConnectorName: 'WeebCentral', objId: 's1', idOnConnectorSite: '01ABC', websiteUrl: null, useForDownload: false },
    ],
    fileLibraryId: null,
    originalLanguage: 'en',
    coverUrl: '',
};

let chapters: object[] | null = [];
let changeLibraryQuery: Record<string, string> | null = null;

registerEndpoint('/v2/FileLibrary', () => [{ key: 'lib1', libraryName: 'Manga', basePath: '/data/manga' }]);
registerEndpoint('/v2/SeriesSource', () => [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'Indexers', name: 'Indexers', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'Torrent', contentType: 'Comic' },
    { key: 'GetComics', name: 'GetComics', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
]);
registerEndpoint('/v2/Search/WeebCentral/Chapters', (event) => {
    if (chapters === null) throw createError({ statusCode: 500, statusMessage: 'chapter list request failed: HTTP 404' });
    return chapters;
});
registerEndpoint('/v2/Series/s1/ChangeLibrary/lib1', {
    method: 'POST',
    handler: (event) => {
        changeLibraryQuery = getQuery(event) as Record<string, string>;
        return {};
    },
});

function bodyText() {
    return document.body.textContent ?? '';
}

function findButton(label: string): HTMLButtonElement {
    const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label));
    expect(button, `button "${label}"`).toBeTruthy();
    return button as HTMLButtonElement;
}

describe('AddSeriesModal', () => {
    beforeEach(() => {
        chapters = [];
        changeLibraryQuery = null;
        document.body.innerHTML = '';
    });

    it('previews how many chapters the source will actually yield', async () => {
        chapters = Array.from({ length: 22 }, (_, i) => ({ chapterNumber: `${i + 1}`, volumeNumber: null, title: null }));
        await mountSuspended(AddSeriesModal, { props: { series, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('22 chapters'));
    });

    it('warns when the source reports no chapters, before the user commits', async () => {
        chapters = [];
        await mountSuspended(AddSeriesModal, { props: { series, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('no chapters'));
    });

    it('blocks adding and offers a search when the source reports no chapters', async () => {
        chapters = [];
        await mountSuspended(AddSeriesModal, { props: { series, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('no chapters'));
        // Adding would download nothing, so the add buttons are gone; a search to find another source replaces them.
        expect([...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('Add & download'))).toBe(false);
        expect([...document.body.querySelectorAll('button')].some((b) => b.textContent?.includes('Add only'))).toBe(false);

        findButton('Search other sources').click();
        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Fire%20Punch'));
    });

    it('surfaces a broken source instead of a silent empty preview', async () => {
        chapters = null;
        await mountSuspended(AddSeriesModal, { props: { series, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('could not deliver a chapter list'));
    });

    it('Add & download adds to the chosen library with download=true and emits added', async () => {
        chapters = [{ chapterNumber: '1', volumeNumber: null, title: null }];
        const wrapper = await mountSuspended(AddSeriesModal, { props: { series, open: true } });
        await vi.waitFor(() => expect(bodyText()).toContain('1 chapter'));

        findButton('Add & download').click();

        await vi.waitFor(() =>
            expect(changeLibraryQuery).toMatchObject({ connectorName: 'WeebCentral', connectorSeriesId: '01ABC', download: 'true' }));
        expect(wrapper.emitted('added')).toBeTruthy();
    });

    it('describes a direct-download comic source without claiming indexer delivery', async () => {
        chapters = [{ chapterNumber: '1', volumeNumber: null, title: null }];
        const comicSeries = {
            ...series,
            sourceIds: [{ ...series.sourceIds[0], mangaConnectorName: 'GetComics' }],
        };
        await mountSuspended(AddSeriesModal, { props: { series: comicSeries, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('From GetComics — comic'));
        expect(bodyText()).not.toContain('indexers');
    });

    it('still explains indexer delivery for torrent-backed comics', async () => {
        chapters = [{ chapterNumber: '1', volumeNumber: null, title: null }];
        const comicSeries = {
            ...series,
            sourceIds: [{ ...series.sourceIds[0], mangaConnectorName: 'Indexers' }],
        };
        await mountSuspended(AddSeriesModal, { props: { series: comicSeries, open: true } });

        await vi.waitFor(() => expect(bodyText()).toContain('From Indexers — comic, delivered via your indexers'));
    });

    it('Add only sends download=false', async () => {
        chapters = [{ chapterNumber: '1', volumeNumber: null, title: null }];
        await mountSuspended(AddSeriesModal, { props: { series, open: true } });
        await vi.waitFor(() => expect(bodyText()).toContain('1 chapter'));

        findButton('Add only').click();

        await vi.waitFor(() => expect(changeLibraryQuery).toMatchObject({ download: 'false' }));
    });
});
