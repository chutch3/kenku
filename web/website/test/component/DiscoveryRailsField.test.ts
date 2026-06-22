import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import DiscoveryRailsField from '~/components/DiscoveryRailsField.vue';

let patched: string[] | null = null;

registerEndpoint('/v2/Settings', () => ({
    apiKey: '', metronConfigured: false, torrentEnabled: false,
    discoveryGenres: [], discoveryFeeds: [], discoveryRails: ['manga-trending'], syncedIndexers: [], downloadClients: [],
}));
registerEndpoint('/v2/Discover/RailCatalog', () => [
    { id: 'manga-trending', label: 'Trending', contentType: 'Manga', order: 10 },
    { id: 'comics-fresh', label: 'Fresh releases', contentType: 'Comic', order: 100 },
]);
registerEndpoint('/v2/Settings/DiscoveryRails', {
    method: 'PATCH',
    handler: async (event) => { patched = await readBody(event); return {}; },
});

describe('DiscoveryRailsField', () => {
    beforeEach(() => {
        patched = null;
        clearNuxtData();
    });

    it('lists every catalog rail (incl. a disabled one) and toggling one off PATCHes the denylist', async () => {
        const wrapper = await mountSuspended(DiscoveryRailsField);

        // 'comics-fresh' is on (not in the denylist); turning it off adds it to the disabled ids.
        const sw = await vi.waitFor(() => {
            const el = wrapper.find('[aria-label="Show the Fresh releases rail"]');
            expect(el.exists()).toBe(true);
            return el;
        });
        expect(sw.attributes('aria-checked')).toBe('true');

        await sw.trigger('click');

        await vi.waitFor(() => expect(patched).toEqual(['manga-trending', 'comics-fresh']));
    });
});
