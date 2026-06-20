import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { flushPromises } from '@vue/test-utils';
import TorrentFeatureCard from '~/components/TorrentFeatureCard.vue';

let patchedTo: boolean | null = null;

function settings(torrentEnabled: boolean) {
    return {
        apiKey: '', metronConfigured: false, torrentEnabled,
        discoveryGenres: [], discoveryFeeds: [], syncedIndexers: [], downloadClients: [],
    };
}

// useSettings pulls these three; register them so the component mounts.
registerEndpoint('/v2/LibraryConnector', () => []);
registerEndpoint('/v2/Stats', () => ({}));
registerEndpoint('/v2/Settings/TorrentEnabled/true', { method: 'PATCH', handler: () => { patchedTo = true; return {}; } });
registerEndpoint('/v2/Settings/TorrentEnabled/false', { method: 'PATCH', handler: () => { patchedTo = false; return {}; } });

describe('TorrentFeatureCard', () => {
    beforeEach(() => {
        patchedTo = null;
        clearNuxtData();
    });

    it('reflects the current state and PATCHes the new one when toggled', async () => {
        registerEndpoint('/v2/Settings', () => settings(false));
        const wrapper = await mountSuspended(TorrentFeatureCard);
        const sw = await vi.waitFor(() => {
            const el = wrapper.find('[role="switch"]');
            expect(el.exists()).toBe(true);
            return el;
        });
        expect(sw.attributes('aria-checked')).toBe('false');

        await sw.trigger('click');
        await flushPromises();

        await vi.waitFor(() => expect(patchedTo).toBe(true));
    });
});
