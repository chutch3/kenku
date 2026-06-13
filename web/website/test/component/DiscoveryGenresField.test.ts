import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { createError, readBody } from 'h3';
import DiscoveryGenresField from '~/components/DiscoveryGenresField.vue';

let patchedBody: unknown = null;
let patchFails = false;

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
        if (patchFails) throw createError({ statusCode: 500, statusMessage: 'save failed' });
        patchedBody = await readBody(event);
        return {};
    },
});

describe('DiscoveryGenresField', () => {
    beforeEach(() => {
        patchedBody = null;
        patchFails = false;
        clearNuxtData();
    });

    it('reverts the chip when the save fails so the UI never lies about what is saved', async () => {
        patchFails = true;
        const wrapper = await mountSuspended(DiscoveryGenresField);
        await vi.waitFor(() => expect(wrapper.text()).toContain('Action'));

        const input = wrapper.find('input');
        await input.setValue('Horror');
        await input.trigger('keydown', { key: 'Enter' });

        await vi.waitFor(() => expect(wrapper.text()).not.toContain('Horror'));
        expect(wrapper.text()).toContain('Action');
        expect(wrapper.text()).toContain('Romance');
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
