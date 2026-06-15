import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import type { VueWrapper } from '@vue/test-utils';
import type { components } from '#open-fetch-schemas/api';
import SeriesCard from '~/components/SeriesCard.vue';
import { tooltipStub } from './tooltipStub';

type SeriesRollup = components['schemas']['SeriesRollup'];

const connectors = [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: 'http://localhost/icon.png', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'GetComics', name: 'GetComics', enabled: true, iconUrl: 'http://localhost/icon.png', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
];

registerEndpoint('/v2/SeriesSource', () => connectors);
registerEndpoint('/v2/SeriesSource/WeebCentral', () => connectors[0]);
registerEndpoint('/v2/SeriesSource/GetComics', () => connectors[1]);

let syncedManga: string | null = null;
registerEndpoint('/v2/Series/s1/Sync', { method: 'POST', handler: () => ((syncedManga = 's1'), {}) });

function series(connectorName: string, fileLibraryId: string | null = null) {
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
        fileLibraryId,
        originalLanguage: 'en',
        coverUrl: 'http://localhost/cover.jpg',
    };
}

function mount(props: Record<string, unknown>) {
    return mountSuspended(SeriesCard, { props, global: { stubs: tooltipStub } });
}

function statusBar(wrapper: VueWrapper) {
    return wrapper.find('[data-test="status-bar"]');
}

describe('SeriesCard', () => {
    it('states the content type on the info line so mixed search results are tellable apart', async () => {
        const wrapper = await mount({ series: series('GetComics') });

        await vi.waitFor(() => expect(wrapper.text()).toContain('Comic · Not tracked'));
    });

    it('manga series read Manga on the info line', async () => {
        const wrapper = await mount({ series: series('WeebCentral') });

        await vi.waitFor(() => expect(wrapper.text()).toContain('Manga · Not tracked'));
    });

    it('colors the status bar by track state', async () => {
        const untracked = await mount({ series: series('GetComics') });
        expect(statusBar(untracked).classes()).toContain('bg-sumi-400/60');

        const attention: Partial<SeriesRollup> = {
            mangaId: 's1', needsAttentionJobs: 2, queuedJobs: 0, runningJobs: 0, downloadedChapters: 1, wantedChapters: 12,
        };
        const broken = await mount({ series: series('GetComics', 'lib1'), rollup: attention });
        expect(statusBar(broken).classes()).toContain('bg-vermillion-500');
    });

    it('shows downloaded / wanted progress from the rollup, no extra fetch', async () => {
        const rollup: Partial<SeriesRollup> = {
            mangaId: 's1', needsAttentionJobs: 0, queuedJobs: 1, runningJobs: 0, downloadedChapters: 5, wantedChapters: 12,
        };
        const wrapper = await mount({ series: series('WeebCentral', 'lib1'), rollup });

        await vi.waitFor(() => expect(wrapper.find('[data-test="card-progress"]').exists()).toBe(true));
        expect(wrapper.find('[data-test="card-progress"]').text()).toContain('5/12');
    });

    it('offers a Retry that re-syncs the series when it needs attention', async () => {
        syncedManga = null;
        const rollup: Partial<SeriesRollup> = {
            mangaId: 's1', needsAttentionJobs: 1, queuedJobs: 0, runningJobs: 0, downloadedChapters: 1, wantedChapters: 12,
            lastError: 'WeebCentral returned 403',
        };
        const wrapper = await mount({ series: series('WeebCentral', 'lib1'), rollup });

        await vi.waitFor(() => expect(wrapper.text()).toContain('WeebCentral returned 403'));
        const retry = wrapper.findAll('button').find((b) => b.text().includes('Retry'));
        expect(retry, 'Retry button').toBeTruthy();
        await retry!.trigger('click');
        await vi.waitFor(() => expect(syncedManga).toBe('s1'));
    });
});
