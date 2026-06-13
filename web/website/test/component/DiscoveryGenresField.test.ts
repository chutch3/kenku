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
    discoveryFeeds: ['manga'],
    syncedIndexers: [],
    downloadClients: [],
}));
registerEndpoint('/v2/Discover/Genres', () => ['Action', 'Horror', 'Romance', 'Sci-Fi', 'Thriller']);
registerEndpoint('/v2/Settings/DiscoveryGenres', {
    method: 'PATCH',
    handler: async (event) => {
        if (patchFails) throw createError({ statusCode: 500, statusMessage: 'save failed' });
        patchedBody = await readBody(event);
        return {};
    },
});

const findSelect = (wrapper: Awaited<ReturnType<typeof mountSuspended>>) =>
    wrapper.findComponent({ name: 'USelectMenu' });

describe('DiscoveryGenresField', () => {
    beforeEach(() => {
        patchedBody = null;
        patchFails = false;
        clearNuxtData();
    });

    it('offers only AniList genres and pre-selects the configured ones', async () => {
        const wrapper = await mountSuspended(DiscoveryGenresField);

        await vi.waitFor(() => expect(findSelect(wrapper).props('items')).toContain('Action'));
        const select = findSelect(wrapper);
        expect(select.props('items')).toEqual(['Action', 'Horror', 'Romance', 'Sci-Fi', 'Thriller']);
        expect(select.props('items')).not.toContain('Gore');
        expect(select.props('modelValue')).toEqual(['Action', 'Romance']);
    });

    it('saves the new selection when genres change', async () => {
        const wrapper = await mountSuspended(DiscoveryGenresField);
        await vi.waitFor(() => expect(findSelect(wrapper).props('modelValue')).toEqual(['Action', 'Romance']));

        await findSelect(wrapper).vm.$emit('update:modelValue', ['Action', 'Romance', 'Horror']);

        await vi.waitFor(() => expect(patchedBody).toEqual(['Action', 'Romance', 'Horror']));
    });

    it('reverts the selection when the save fails', async () => {
        patchFails = true;
        const wrapper = await mountSuspended(DiscoveryGenresField);
        await vi.waitFor(() => expect(findSelect(wrapper).props('modelValue')).toEqual(['Action', 'Romance']));

        await findSelect(wrapper).vm.$emit('update:modelValue', ['Action', 'Romance', 'Horror']);

        await vi.waitFor(() => expect(findSelect(wrapper).props('modelValue')).toEqual(['Action', 'Romance']));
    });
});
