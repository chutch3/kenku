import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import SourcesTable from '~/components/SourcesTable.vue';

const connectors = [
    { key: 'Global', name: 'Global', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList', contentType: 'Manga' },
    { key: 'Indexers', name: 'Indexers', enabled: false, iconUrl: '', supportedLanguages: ['en'], kind: 'Torrent', contentType: 'Comic' },
];

let patched: { name: string; enabled: string } | null = null;

registerEndpoint('/v2/SeriesSource', () => connectors);
registerEndpoint('/v2/SeriesSource/Indexers/SetEnabled/true', {
    method: 'PATCH',
    handler: () => {
        patched = { name: 'Indexers', enabled: 'true' };
        return {};
    },
});

describe('SourcesTable', () => {
    beforeEach(() => {
        patched = null;
        clearNuxtData();
    });

    it('lists real sources with their enabled state, hiding the Global pseudo-source', async () => {
        const wrapper = await mountSuspended(SourcesTable);

        await vi.waitFor(() => expect(wrapper.text()).toContain('WeebCentral'));
        expect(wrapper.text()).toContain('Indexers');
        expect(wrapper.text()).not.toContain('Global');
        expect(wrapper.findAll('[role="switch"]')).toHaveLength(2);
    });

    it('toggling a source patches its enabled flag', async () => {
        const wrapper = await mountSuspended(SourcesTable);
        await vi.waitFor(() => expect(wrapper.findAll('[role="switch"]')).toHaveLength(2));

        const indexersSwitch = wrapper.findAll('[role="switch"]')[1]!;
        await indexersSwitch.trigger('click');

        await vi.waitFor(() => expect(patched).toEqual({ name: 'Indexers', enabled: 'true' }));
    });
});
