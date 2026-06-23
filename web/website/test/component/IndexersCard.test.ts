import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import IndexersCard from '~/components/IndexersCard.vue';

let deleted: string | null = null;

registerEndpoint('/v2/Settings', () => ({
    apiKey: 'KEY',
    metronConfigured: false,
    torrentEnabled: false,
    discoveryGenres: [],
    discoveryFeeds: [],
    discoveryRails: [],
    syncedIndexers: [],
    manualIndexers: [{ name: 'Nyaa', url: 'http://nyaa.test/api', categories: [7030], cooldownUntil: null }],
    downloadClients: [],
}));
registerEndpoint('/v2/LibraryConnector', () => []);
registerEndpoint('/v2/Stats', () => ({}));
registerEndpoint('/v2/Settings/ManualIndexers/Nyaa', {
    method: 'DELETE',
    handler: () => { deleted = 'Nyaa'; return {}; },
});

describe('IndexersCard — manual indexers', () => {
    beforeEach(() => {
        deleted = null;
        clearNuxtData();
    });

    it('lists the configured manual indexers', async () => {
        const wrapper = await mountSuspended(IndexersCard);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Nyaa'));
        expect(wrapper.text()).toContain('http://nyaa.test/api');
    });

    it('removes a manual indexer', async () => {
        const wrapper = await mountSuspended(IndexersCard);
        const btn = await vi.waitFor(() => {
            const el = wrapper.find('[aria-label="Remove Nyaa"]');
            expect(el.exists()).toBe(true);
            return el;
        });
        await btn.trigger('click');
        await vi.waitFor(() => expect(deleted).toBe('Nyaa'));
    });
});
