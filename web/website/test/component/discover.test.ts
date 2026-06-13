import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint, mockNuxtImport } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { createError, getQuery } from 'h3';
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
let globalSearchQuery: Record<string, string> | null = null;
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
const discoveryGenres = ['Action'];
let feedEntries: object[] = [];
registerEndpoint('/v2/Settings', () => ({
    apiKey: '',
    metronConfigured: false,
    discoveryGenres,
    discoveryFeeds: ['manga', 'comicbooks'],
    syncedIndexers: [],
    downloadClients: [],
}));
registerEndpoint('/v2/Discover/Feed', () => feedEntries);
registerEndpoint('/v2/Series', () => []);
registerEndpoint('/v2/Search', async () => {
    // Small delay so tests can observe the modal's resolving state before the match lands.
    await new Promise((r) => setTimeout(r, 50));
    if (urlResolution === null) throw createError({ statusCode: 500, statusMessage: 'resolution failed' });
    return urlResolution;
});
registerEndpoint('/v2/Search/Global/Berserk', (event) => {
    globalSearchQuery = getQuery(event) as Record<string, string>;
    return globalResults;
});
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
        globalSearchQuery = null;
        mangaEntries = defaultManga;
        comicsEntries = defaultComics;
        feedEntries = [];
        navigateToMock.mockClear();
        clearNuxtData();
    });

    it('separates manga and comics recommendations under their own section headings', async () => {
        await mountPage();

        await vi.waitFor(() => expect(wrapper.text()).toContain('Berserk'));
        const headings = wrapper.findAll('h2').map((h) => h.text());
        expect(headings).toContain('Manga');
        expect(headings).toContain('Comics');
    });

    it('explains an empty feed rail instead of hiding it silently', async () => {
        await mountPage();

        await vi.waitFor(() => expect(wrapper.text()).toContain('Nothing from the feeds yet'));
    });

    it('hides the empty-feed note once the feed has posts', async () => {
        feedEntries = [{ title: 'A hot thread', coverUrl: '', url: 'https://reddit.test/1', source: 'r/manga', blurb: null }];
        await mountPage();

        await vi.waitFor(() => expect(wrapper.text()).toContain('A hot thread'));
        expect(wrapper.text()).not.toContain('Nothing from the feeds yet');
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

    it('opens the modal immediately and resolves the comic post inside it', async () => {
        await mountPage();

        await clickCard('Saga');

        // Feedback is instant: the modal is up with the entry's details while the lookup runs.
        await vi.waitFor(() => expect(document.body.textContent).toContain('Finding it on your sources'));
        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('resolves a trending card via a manga-only, torrent-free search', async () => {
        globalResults = [berserkSeries];
        await mountPage();

        await clickCard('Berserk');

        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(globalSearchQuery).toMatchObject({ contentType: 'Manga', includeTorrents: 'false' });
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('offers the search page when no hit matches the title', async () => {
        globalResults = [{ ...berserkSeries, name: 'Berserk: The Prototype' }];
        await mountPage();

        await clickCard('Berserk');

        const fallback = await vi.waitFor(() => {
            const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes('Search instead'));
            expect(button, 'fallback button').toBeTruthy();
            return button!;
        });
        fallback.click();

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Berserk'));
        expect(document.body.textContent).not.toContain('Add & download');
    });

    it('offers the source-scoped search page when URL resolution fails', async () => {
        urlResolution = null;
        await mountPage();

        await clickCard('Saga');

        const fallback = await vi.waitFor(() => {
            const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes('Search instead'));
            expect(button, 'fallback button').toBeTruthy();
            return button!;
        });
        fallback.click();

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Saga&source=GetComics'));
    });
});
