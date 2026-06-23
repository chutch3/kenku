import { describe, it, expect } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import SeriesCardList from '~/components/SeriesCardList.vue';
import { tooltipStub } from './tooltipStub';

registerEndpoint('/v2/SeriesSource', () => [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
]);

const series = [{
    key: 's1', name: 'The Boys', description: '', releaseStatus: 'Continuing',
    sourceIds: [{ key: 'sid1', seriesSourceName: 'WeebCentral', objId: 's1', idOnConnectorSite: 'the-boys', websiteUrl: null, useForDownload: false }],
    fileLibraryId: null, originalLanguage: 'en', coverUrl: '',
}];

describe('SeriesCardList', () => {
    it('exposes each card as a keyboard-operable button (focusable + Enter selects it)', async () => {
        const wrapper = await mountSuspended(SeriesCardList, { props: { series }, global: { stubs: tooltipStub } });

        const card = wrapper.find('[role="button"]');
        expect(card.exists(), 'card has role=button').toBe(true);
        expect(card.attributes('tabindex')).toBe('0');

        await card.trigger('keydown.enter');

        expect(wrapper.emitted('click')).toBeTruthy();
        expect(wrapper.emitted('click')![0][0]).toMatchObject({ key: 's1' });
    });
});
