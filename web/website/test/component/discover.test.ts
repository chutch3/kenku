import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint, mockNuxtImport } from '@nuxt/test-utils/runtime';
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

registerEndpoint('/v2/Discover/Manga', () => [
    { title: 'Berserk', coverUrl: '', url: 'https://anilist.co/manga/1', source: 'AniList', blurb: null },
]);
registerEndpoint('/v2/Discover/Comics', () => [
    { title: 'Saga', coverUrl: '', url: 'https://getcomics.org/other-comics/saga-61-2025/', source: 'GetComics', blurb: null },
]);
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

async function clickCard(wrapper: Awaited<ReturnType<typeof mountSuspended>>, title: string) {
    await vi.waitFor(() => expect(wrapper.text()).toContain(title));
    const card = wrapper.findAll('button').find((b) => b.text().includes(title));
    expect(card, `card "${title}"`).toBeTruthy();
    await card!.trigger('click');
}

describe('discover page', () => {
    beforeEach(() => {
        urlResolution = sagaSeries;
        globalResults = [];
        navigateToMock.mockClear();
        document.body.innerHTML = '';
    });

    it('resolves a fresh-comics card by post URL and opens the add modal in place', async () => {
        const wrapper = await mountSuspended(Discover);

        await clickCard(wrapper, 'Saga');

        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('opens the add modal for a trending card whose title matches a Global hit', async () => {
        globalResults = [berserkSeries];
        const wrapper = await mountSuspended(Discover);

        await clickCard(wrapper, 'Berserk');

        await vi.waitFor(() => expect(document.body.textContent).toContain('Add & download'));
        expect(navigateToMock).not.toHaveBeenCalled();
    });

    it('falls back to the prefilled search page when no Global hit matches the title', async () => {
        globalResults = [{ ...berserkSeries, name: 'Berserk: The Prototype' }];
        const wrapper = await mountSuspended(Discover);

        await clickCard(wrapper, 'Berserk');

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Berserk'));
        expect(document.body.textContent).not.toContain('Add & download');
    });

    it('falls back to the source-scoped search page when URL resolution fails', async () => {
        urlResolution = null;
        const wrapper = await mountSuspended(Discover);

        await clickCard(wrapper, 'Saga');

        await vi.waitFor(() => expect(navigateToMock).toHaveBeenCalledWith('/search?q=Saga&source=GetComics'));
    });
});
