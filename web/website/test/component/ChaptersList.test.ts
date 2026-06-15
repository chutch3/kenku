import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import ChaptersList from '~/components/ChaptersList.vue';
import { tooltipStub } from './tooltipStub';

function chapter(n: string, volume: number | null = null) {
    return { key: `c${n}`, title: null, volume, chapterNumber: n, fileName: null, downloaded: false, sourceIds: [] };
}

const source = (key: string, group: string | null, useForDownload = false) => ({
    key, mangaConnectorName: 'MangaDex', objId: 'cX', idOnConnectorSite: key, websiteUrl: null, useForDownload, scanGroup: group, language: 'en',
});

registerEndpoint('/v2/Chapters/Series/comic-1', { method: 'POST', handler: () => ({ data: [chapter('1'), chapter('2')], totalCount: 2 }) });
registerEndpoint('/v2/Chapters/Series/manga-1', { method: 'POST', handler: () => ({ data: [chapter('1', 3)], totalCount: 1 }) });

// One chapter with a single upload — a plain toggle.
registerEndpoint('/v2/Chapters/Series/single-1', {
    method: 'POST',
    handler: () => ({ data: [{ key: 'cX', title: null, volume: 1, chapterNumber: '1', fileName: null, downloaded: false, sourceIds: [source('src-only', 'Solo Group')] }], totalCount: 1 }),
});
// One chapter with several uploads — the chooser.
registerEndpoint('/v2/Chapters/Series/multi-1', {
    method: 'POST',
    handler: () => ({ data: [{ key: 'cX', title: null, volume: 44, chapterNumber: '384', fileName: null, downloaded: false, sourceIds: [source('src-a', 'Group A'), source('src-b', 'Group B')] }], totalCount: 1 }),
});

let pickedSource: string | null = null;
registerEndpoint('/v2/Chapters/Source/src-b/DownloadFrom/true', { method: 'PATCH', handler: () => ((pickedSource = 'src-b'), {}) });
registerEndpoint('/v2/Chapters/Source/src-only/DownloadFrom/true', { method: 'PATCH', handler: () => ((pickedSource = 'src-only'), {}) });

function mount(mangaId: string, kind?: string) {
    return mountSuspended(ChaptersList, { props: { mangaId, kind }, global: { stubs: tooltipStub } });
}

describe('ChaptersList', () => {
    it('comic series list issues: #N rows, an issue count, and no volume filter', async () => {
        const wrapper = await mount('comic-1', 'comic');

        await vi.waitFor(() => expect(wrapper.text()).toContain('2 issues'));
        expect(wrapper.text()).toContain('#1');
        expect(wrapper.text()).not.toContain('Ch.');
        expect(wrapper.findAll('input').map((i) => i.attributes('placeholder'))).not.toContain('Vol');
    });

    it('manga keep chapter wording and the volume filter', async () => {
        const wrapper = await mount('manga-1');

        await vi.waitFor(() => expect(wrapper.text()).toContain('1 chapters'));
        expect(wrapper.text()).toContain('Vol. 3');
        expect(wrapper.text()).toContain('Ch. 1');
        expect(wrapper.findAll('input').map((i) => i.attributes('placeholder'))).toContain('Vol');
    });

    it('downloads a single-upload chapter from its one source', async () => {
        pickedSource = null;
        const wrapper = await mount('single-1');
        await vi.waitFor(() => expect(wrapper.text()).toContain('Ch. 1'));

        const download = wrapper.find('[data-test="download-src-only"]');
        expect(download.exists()).toBe(true);
        await download.trigger('click');

        await vi.waitFor(() => expect(pickedSource).toBe('src-only'));
    });

    it('offers a chooser when a chapter has several uploads, and downloads the picked one', async () => {
        pickedSource = null;
        const wrapper = await mount('multi-1');
        await vi.waitFor(() => expect(wrapper.text()).toContain('Ch. 384'));

        // One choose affordance, not one toggle per upload.
        const choose = wrapper.find('[data-test="choose-download"]');
        expect(choose.exists()).toBe(true);
        await choose.trigger('click');

        // The chooser lists the scan groups (modal teleports to body).
        await vi.waitFor(() => expect(document.body.textContent).toContain('Group A'));
        expect(document.body.textContent).toContain('Group B');

        const pickB = document.body.querySelector('[data-test="pick-src-b"]') as HTMLButtonElement;
        expect(pickB, 'pick button for Group B').toBeTruthy();
        pickB.click();

        await vi.waitFor(() => expect(pickedSource).toBe('src-b'));
    });
});
