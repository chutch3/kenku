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

    it('shows the configured genres and saves the list', async () => {
        const wrapper = await mountSuspended(DiscoveryGenresField);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Action'));
        expect(wrapper.text()).toContain('Romance');

        const save = wrapper.findAll('button').find((b) => b.text().includes('Save'));
        expect(save, 'save button').toBeTruthy();
        await save!.trigger('click');

        await vi.waitFor(() => expect(patchedBody).toEqual(['Action', 'Romance']));
    });
});
