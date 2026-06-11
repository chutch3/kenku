import { describe, it, expect } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import LibraryLayoutSelect from '~/components/LibraryLayoutSelect.vue';

registerEndpoint('/v2/Series/comic-1/volumes', () => ({ filesNeedReorganizing: 0, layout: 'Flat', volumes: [], unassigned: [] }));
registerEndpoint('/v2/Series/manga-1/volumes', () => ({ filesNeedReorganizing: 0, layout: 'Flat', volumes: [], unassigned: [] }));

describe('LibraryLayoutSelect', () => {
    it('explains the layout trade-off for comics', async () => {
        const wrapper = await mountSuspended(LibraryLayoutSelect, { props: { mangaId: 'comic-1', kind: 'comic' } });

        expect(wrapper.text().toLowerCase()).toContain('comics download as finished archives');
    });

    it('shows no comic hint for manga', async () => {
        const wrapper = await mountSuspended(LibraryLayoutSelect, { props: { mangaId: 'manga-1', kind: 'manga' } });

        expect(wrapper.text().toLowerCase()).not.toContain('finished archives');
    });
});
