import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { defineComponent, h } from 'vue';

registerEndpoint('/v2/Series/Rollup', () => [
    { mangaId: 'm1', wantedChapters: 10, downloadedChapters: 5, queuedJobs: 0, runningJobs: 0, needsAttentionJobs: 0, lastError: null, lastSyncAt: null, lastSyncChapterCount: null },
]);
registerEndpoint('/v2/SeriesSource', () => [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
]);
registerEndpoint('/v2/Series/m1', () => ({
    key: 'm1', name: 'Berserk', description: '', releaseStatus: 'Continuing',
    sourceIds: [{ key: 'sid', mangaConnectorName: 'WeebCentral', objId: 'm1', idOnConnectorSite: 'x', websiteUrl: null, useForDownload: true }],
    fileLibraryId: 'lib1', originalLanguage: 'en', coverUrl: '', authors: [], tags: [], links: [], altTitles: [], ignoreChaptersBefore: 0,
}));

// A harness exposes the composable's reactive output; useSeriesDetail is auto-imported.
const Harness = defineComponent({
    setup() {
        const d = useSeriesDetail('m1');
        return () => h('div', [
            h('span', { class: 'name' }, d.series.value?.name ?? ''),
            h('span', { class: 'kind' }, d.kind.value),
            h('span', { class: 'wanted' }, String(d.rollup.value?.wantedChapters ?? '')),
        ]);
    },
});

describe('useSeriesDetail', () => {
    it('exposes the series, its matching rollup, and the kind — no manual ref mirror', async () => {
        const wrapper = await mountSuspended(Harness);

        await vi.waitFor(() => expect(wrapper.find('.name').text()).toBe('Berserk'));
        expect(wrapper.find('.kind').text()).toBe('manga');
        await vi.waitFor(() => expect(wrapper.find('.wanted').text()).toBe('10'));
    });
});
