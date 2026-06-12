import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import DiscoveryGenresField from '~/components/DiscoveryGenresField.vue';

let patchedBody: unknown = null;

registerEndpoint('/v2/Settings', () => ({
    apiKey: '',
    metronConfigured: false,
    discoveryGenres: ['Action', 'Romance'],
    syncedIndexers: [],
    downloadClients: [],
}));
registerEndpoint('/v2/Settings/DiscoveryGenres', {
    method: 'PATCH',
    handler: async (event) => {
        patchedBody = await readBody(event);
        return {};
    },
});

describe('DiscoveryGenresField', () => {
    beforeEach(() => {
        patchedBody = null;
        clearNuxtData();
    });

    it('shows the configured genres and saves as soon as one is committed', async () => {
        const wrapper = await mountSuspended(DiscoveryGenresField);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Action'));
        expect(wrapper.text()).toContain('Romance');

        const input = wrapper.find('input');
        await input.setValue('Horror');
        await input.trigger('keydown', { key: 'Enter' });

        await vi.waitFor(() => expect(patchedBody).toEqual(['Action', 'Romance', 'Horror']));
    });
});
