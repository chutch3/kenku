import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import TorrentsPanel from '~/components/TorrentsPanel.vue';

let torrents: object[] = [];

registerEndpoint('/v2/Torrents', () => torrents);

describe('TorrentsPanel', () => {
    beforeEach(() => {
        torrents = [];
        clearNuxtData();
    });

    it('shows each in-flight torrent with progress and seeders', async () => {
        torrents = [
            { name: 'Saga 060 (2024)', tag: 'chap-1', state: 'downloading', progress: 0.42, seeders: 12, error: null },
            { name: 'Invincible 001-144', tag: 'pack:s1:abcd', state: 'completed', progress: 1, seeders: 3, error: null },
        ];
        const wrapper = await mountSuspended(TorrentsPanel);

        await vi.waitFor(() => expect(wrapper.text()).toContain('Saga 060 (2024)'));
        expect(wrapper.text()).toContain('42%');
        expect(wrapper.text()).toContain('12 seeders');
        expect(wrapper.text()).toContain('Invincible 001-144');
    });

    it('renders nothing while the client holds no torrents', async () => {
        const wrapper = await mountSuspended(TorrentsPanel);

        await vi.waitFor(() => expect(wrapper.text()).toBe(''));
    });
});
