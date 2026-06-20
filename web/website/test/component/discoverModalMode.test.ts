import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { getQuery } from 'h3';
import DiscoverAddModal from '~/components/DiscoverAddModal.vue';

// Own file so the media-mode useState is fresh and seeds from the cookie set below.
document.cookie = 'media-mode=comic';

let connectorContentType: string | undefined;

registerEndpoint('/v2/Search/Global/MangaCard', (event) => {
    connectorContentType = String(getQuery(event).contentType ?? '');
    return [];
});
registerEndpoint('/v2/SeriesSource', () => []);
registerEndpoint('/v2/FileLibrary', () => []);

describe('DiscoverAddModal media mode', () => {
    it('resolves a source-less entry with the active mode content type, not a hardcoded Manga', async () => {
        const entry = { title: 'MangaCard', coverUrl: '', url: null, source: null, blurb: null };
        const wrapper = await mountSuspended(DiscoverAddModal, { props: { entry, open: true } });

        await vi.waitFor(() => expect(connectorContentType).toBe('Comic'));

        wrapper.unmount();
    });
});
