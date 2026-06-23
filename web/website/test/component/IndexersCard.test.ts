import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import IndexersCard from '~/components/IndexersCard.vue';

let posted: Record<string, unknown> | null = null;
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
registerEndpoint('/v2/Settings/ManualIndexers', {
    method: 'POST',
    handler: async (event) => { posted = await readBody(event); return {}; },
});
registerEndpoint('/v2/Settings/ManualIndexers/Nyaa', {
    method: 'DELETE',
    handler: () => { deleted = 'Nyaa'; return {}; },
});

describe('IndexersCard — manual indexers', () => {
    beforeEach(() => {
        posted = null;
        deleted = null;
        clearNuxtData();
    });

    it('lists the configured manual indexers', async () => {
        const wrapper = await mountSuspended(IndexersCard);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Nyaa'));
        expect(wrapper.text()).toContain('http://nyaa.test/api');
    });

    it('adds a manual indexer (POSTs name, url, key, parsed categories)', async () => {
        const wrapper = await mountSuspended(IndexersCard);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Nyaa'));

        await wrapper.find('[placeholder="Indexer name"]').setValue('AnimeBytes');
        await wrapper.find('[placeholder="Torznab/Newznab URL"]').setValue('http://ab.test/api');
        await wrapper.find('[placeholder="API key"]').setValue('secret');
        await wrapper.find('[placeholder="Categories (e.g. 7030, 7000)"]').setValue('7030, 7000');
        await wrapper.find('[aria-label="Add manual indexer"]').trigger('click');

        await vi.waitFor(() => expect(posted).toMatchObject({
            name: 'AnimeBytes', url: 'http://ab.test/api', apiKey: 'secret', categories: [7030, 7000],
        }));
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
