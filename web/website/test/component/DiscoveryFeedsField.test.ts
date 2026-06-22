import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import DiscoveryFeedsField from '~/components/DiscoveryFeedsField.vue';

let patched: string[] | null = null;

registerEndpoint('/v2/Settings', () => ({
    apiKey: '', metronConfigured: false, torrentEnabled: false,
    discoveryGenres: [], discoveryFeeds: ['manga'], discoveryRails: [], syncedIndexers: [], downloadClients: [],
}));
registerEndpoint('/v2/Settings/DiscoveryFeeds', {
    method: 'PATCH',
    handler: async (event) => { patched = await readBody(event); return {}; },
});

describe('DiscoveryFeedsField', () => {
    beforeEach(() => {
        patched = null;
        clearNuxtData();
    });

    it('shows the current feeds and saves the edited list', async () => {
        const wrapper = await mountSuspended(DiscoveryFeedsField);
        await vi.waitFor(() => expect((wrapper.find('input').element as HTMLInputElement).value).toBe('manga'));

        await wrapper.find('input').setValue('manga, comicbooks');
        await wrapper.find('button').trigger('click');

        await vi.waitFor(() => expect(patched).toEqual(['manga', 'comicbooks']));
    });
});
