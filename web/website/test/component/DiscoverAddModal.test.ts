import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { getQuery } from 'h3';
import DiscoverAddModal from '~/components/DiscoverAddModal.vue';

const series = (name: string, connector: string) => ({
    key: `${name}-key`,
    name,
    description: '',
    releaseStatus: 'Continuing',
    sourceIds: [
        { key: `${name}-sid`, mangaConnectorName: connector, objId: `${name}-key`, idOnConnectorSite: name, websiteUrl: null, useForDownload: false },
    ],
    fileLibraryId: null,
    originalLanguage: 'en',
    coverUrl: '',
});

const slowEntry = { title: 'SlowOne', coverUrl: '', url: 'https://getcomics.org/slow-one', source: 'GetComics', blurb: null };
const fastEntry = { title: 'FastOne', coverUrl: '', url: 'https://getcomics.org/fast-one', source: 'GetComics', blurb: null };

// URL resolution returns the post's series unfiltered — exactly the path where a stale response
// could hijack the form for a different card.
registerEndpoint('/v2/Search', async (event) => {
    const url = String(getQuery(event).url ?? '');
    if (url.includes('slow-one')) {
        await new Promise((r) => setTimeout(r, 150));
        return series('SlowOne', 'SlowSource');
    }
    return series('FastOne', 'FastSource');
});
registerEndpoint('/v2/SeriesSource', () => [
    { key: 'SlowSource', name: 'SlowSource', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
    { key: 'FastSource', name: 'FastSource', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'DirectArchive', contentType: 'Comic' },
]);
registerEndpoint('/v2/FileLibrary', () => [{ key: 'lib1', libraryName: 'Comics', basePath: '/data/comics' }]);
registerEndpoint('/v2/Search/SlowSource/Chapters', () => [{ chapterNumber: '1', volumeNumber: null, title: null }]);
registerEndpoint('/v2/Search/FastSource/Chapters', () => [{ chapterNumber: '1', volumeNumber: null, title: null }]);

describe('DiscoverAddModal', () => {
    beforeEach(() => {
        clearNuxtData();
        document.body.innerHTML = '';
    });

    it('ignores a stale resolution after the modal is re-targeted at another entry', async () => {
        const wrapper = await mountSuspended(DiscoverAddModal, { props: { entry: slowEntry, source: 'GetComics', open: true } });

        // Close mid-lookup and re-open on a different card — the old lookup is still in flight.
        await wrapper.setProps({ open: false });
        await wrapper.setProps({ entry: fastEntry });
        await wrapper.setProps({ open: true });

        await vi.waitFor(() => expect(document.body.textContent).toContain('available from FastSource'));
        // Let the slow response land; it must not hijack the form.
        await new Promise((r) => setTimeout(r, 250));
        expect(document.body.textContent).toContain('available from FastSource');
        expect(document.body.textContent).not.toContain('SlowSource');

        wrapper.unmount();
    });
});
