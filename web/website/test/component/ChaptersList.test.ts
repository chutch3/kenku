import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import ChaptersList from '~/components/ChaptersList.vue';
import { tooltipStub } from './tooltipStub';

function chapter(n: string, volume: number | null = null) {
    return { key: `c${n}`, title: null, volume, chapterNumber: n, fileName: null, downloaded: false, sourceIds: [] };
}

registerEndpoint('/v2/Chapters/Series/comic-1', { method: 'POST', handler: () => ({ data: [chapter('1'), chapter('2')], totalCount: 2 }) });
registerEndpoint('/v2/Chapters/Series/manga-1', { method: 'POST', handler: () => ({ data: [chapter('1', 3)], totalCount: 1 }) });

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
});
