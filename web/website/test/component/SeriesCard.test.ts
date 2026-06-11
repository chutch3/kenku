import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import type { VueWrapper } from '@vue/test-utils';
import SeriesCard from '~/components/SeriesCard.vue';
import { tooltipStub } from './tooltipStub';

const connectors = [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: 'http://localhost/icon.png', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'GetComics', name: 'GetComics', enabled: true, iconUrl: 'http://localhost/icon.png', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
];

registerEndpoint('/v2/SeriesSource', () => connectors);
registerEndpoint('/v2/SeriesSource/WeebCentral', () => connectors[0]);
registerEndpoint('/v2/SeriesSource/GetComics', () => connectors[1]);

function series(connectorName: string) {
    return {
        key: 's1',
        name: 'The Boys',
        description: '',
        releaseStatus: 'Continuing',
        sourceIds: [
            {
                key: 'sid1',
                mangaConnectorName: connectorName,
                objId: 's1',
                idOnConnectorSite: 'the-boys',
                websiteUrl: null,
                useForDownload: true,
            },
        ],
        fileLibraryId: null,
        originalLanguage: 'en',
        coverUrl: 'http://localhost/cover.jpg',
    };
}

function mount(connectorName: string) {
    return mountSuspended(SeriesCard, {
        props: { series: series(connectorName) },
        global: { stubs: tooltipStub },
    });
}

function comicBadge(wrapper: VueWrapper) {
    return wrapper.findAll('span').find((s) => s.text() === 'Comic');
}

describe('SeriesCard', () => {
    it('badges a comic-sourced series so mixed search results are tellable apart', async () => {
        const wrapper = await mount('GetComics');

        await vi.waitFor(() => expect(comicBadge(wrapper)).toBeTruthy());
    });

    it('leaves manga-sourced series unbadged', async () => {
        const wrapper = await mount('WeebCentral');

        // Wait until the connector-driven source icon is up, so the missing badge is a decision, not a race.
        await vi.waitFor(() => expect(wrapper.find('img[alt="WeebCentral icon"]').exists()).toBe(true));
        expect(comicBadge(wrapper)).toBeFalsy();
    });
});
