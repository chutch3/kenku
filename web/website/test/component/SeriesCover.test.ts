import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import type { components } from '#open-fetch-schemas/api';
import SeriesCover from '~/components/SeriesCover.vue';

type MinimalSeries = components['schemas']['MinimalSeries'];

function series(fileLibraryId: string | null): MinimalSeries {
    return {
        key: 's1',
        name: 'Berserk',
        description: '',
        releaseStatus: 'Continuing',
        sourceIds: [],
        fileLibraryId,
        originalLanguage: 'en',
        coverUrl: 'https://temp.example/rotted.webp',
    } as MinimalSeries;
}

describe('SeriesCover', () => {
    it("tracked series use Kenku's cached cover first, then the source URL, then the placeholder", async () => {
        const wrapper = await mountSuspended(SeriesCover, { props: { series: series('lib1') } });

        expect(wrapper.find('img').attributes('src')).toContain('/Cover/Medium');

        // Cache miss (e.g. cover never downloaded) → the source URL still gets a chance.
        await wrapper.find('img').trigger('error');
        expect(wrapper.find('img').attributes('src')).toBe('https://temp.example/rotted.webp');

        // Source URL rotted too → placeholder, not a broken image.
        await wrapper.find('img').trigger('error');
        expect(wrapper.find('img').attributes('src')).toContain('kenku.svg');
    });

    it('search results hotlink the source cover first — nothing is cached before adding', async () => {
        const wrapper = await mountSuspended(SeriesCover, { props: { series: series(null) } });

        expect(wrapper.find('img').attributes('src')).toBe('https://temp.example/rotted.webp');
    });
});
