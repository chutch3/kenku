import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint, mockNuxtImport } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { createError } from 'h3';
import Discover from '~/pages/discover.vue';

const { navigateToMock } = vi.hoisted(() => ({ navigateToMock: vi.fn() }));
mockNuxtImport('navigateTo', () => navigateToMock);

const sagaSeries = {
    key: 'saga-key',
    name: 'Saga',
    description: 'Space opera.',
    releaseStatus: 'Continuing',
    sourceIds: [
        { key: 'sid1', mangaConnectorName: 'GetComics', objId: 'saga-key', idOnConnectorSite: 'Saga', websiteUrl: null, useForDownload: false },
    ],
    fileLibraryId: null,
    originalLanguage: 'en',
    coverUrl: '',
};
const berserkSeries = { ...sagaSeries, key: 'berserk-key', name: 'Berserk' };

let urlResolution: object | null = sagaSeries;
let globalResults: object[] = [];
let mangaEntries: object[] = [];
let comicsEntries: object[] = [];

const defaultManga = [{ title: 'Berserk', coverUrl: '', url: 'https://anilist.co/manga/1', source: 'AniList', blurb: null }];
const defaultComics = [
    { title: 'Saga', coverUrl: '', url: 'https://getcomics.org/other-comics/saga-61-2025/', source: 'GetComics', blurb: null },
];

registerEndpoint('/v2/Discover/Manga', () => mangaEntries);
registerEndpoint('/v2/Discover/Comics', () => comicsEntries);
registerEndpoint('/v2/Discover/Manga/TopRated', () => [
    { title: 'Vagabond', coverUrl: '', url: 'https://anilist.co/manga/3', source: 'AniList', blurb: null },
]);
registerEndpoint('/v2/Discover/Manga/New', () => [
    { title: 'Kagurabachi', coverUrl: '', url: 'https://anilist.co/manga/4', source: 'AniList', blurb: null },
]);
registerEndpoint('/v2/Discover/Manga/Genre/Action', () => [
    { title: 'Sakamoto Days', coverUrl: '', url: 'https://anilist.co/manga/5', source: 'AniList', blurb: null },
]);
registerEndpoint('/v2/Settings', () => ({
    apiKey: '',
    metronConfigured: false,
    discoveryGenres: ['Action'],
    syncedIndexers: [],
    downloadClients: [],
}));
registerEndpoint('/v2/Discover/Feed', () => []);
registerEndpoint('/v2/Series', () => []);
registerEndpoint('/v2/Search', () => {
    if (urlResolution === null) throw createError({ statusCode: 500, statusMessage: 'resolution failed' });
    return urlResolution;
});
registerEndpoint('/v2/Search/Global/Berserk', () => globalResults);
registerEndpoint('/v2/SeriesSource', () => [
    { key: 'Global', name: 'Global', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'GetComics', name: 'GetComics', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
]);
registerEndpoint('/v2/FileLibrary', () => [{ key: 'lib1', libraryName: 'Comics', basePath: '/data/comics' }]);
registerEndpoint('/v2/Search/GetComics/Chapters', () => [{ chapterNumber: '61', volumeNumber: null, title: null }]);

let wrapper: Awaited<ReturnType<typeof mountSuspended>>;
const mountPage = async () => (wrapper = await mountSuspended(Discover));

async function clickCard(title: string) {
    await vi.waitFor(() => expect(wrapper.text()).toContain(title));
    const card = wrapper.findAll('button').find((b) => b.text().includes(title));
    expect(card, `card "${title}"`).toBeTruthy();
    await card!.trigger('click');
}

describe('discover page', () => {
    beforeEach(() => {
        urlResolution = sagaSeries;
        globalResults = [];
        mangaEntries = defaultManga;
        comicsEntries = defaultComics;
        navigateToMock.mockClear();
        clearNuxtData();
    });

    // Unmount before wiping the body — a live wrapper re-rendering against a wiped DOM throws.
    afterEach(() => {
        wrapper?.unmount();
        document.body.innerHTML = '';
    });

    it('does not claim there is nothing to show while a newer rail has entries', async () => {
        mangaEntries = [];
        comicsEntries = [];
        await mountPage();

        await vi.waitFor(() => expect(wrapper.text()).toContain('Vagabond'));
        expect(wrapper.text()).not.toContain('Nothing to show');
    });

    it('renders the top-rated, new-this-year, and configured genre rails', async () => {
        await mountPage();

        await vi.waitFor(() => {
            expect(wrapper.text()).toContain('Vagabond');
            expect(wrapper.text()).toContain('Kagurabachi');
            expect(wrapper.text()).toContain('Sakamoto Days');
        });
        expect(wrapper.text()).toContain('Action');
    });

    it('resolves a fresh-comics card by post URL and opens the add modal in place', async () => {
        await mountPage();

        await clickCard('Saga');

        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('opens the add modal for a trending card whose title matches a Global hit', async () => {
        globalResults = [berserkSeries];
        await mountPage();

        await clickCard('Berserk');

        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('falls back to the prefilled search page when no Global hit matches the title', async () => {
        globalResults = [{ ...berserkSeries, name: 'Berserk: The Prototype' }];
        await mountPage();

        await clickCard('Berserk');

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Berserk'));
        expect(document.body.textContent).not.toContain('Add & download');
    });

    it('falls back to the source-scoped search page when URL resolution fails', async () => {
        urlResolution = null;
        await mountPage();

        await clickCard('Saga');

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Saga&source=GetComics'));
    });
});
