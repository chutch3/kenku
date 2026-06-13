import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import DownloadChoiceModal from '~/components/DownloadChoiceModal.vue';

let options: object = {};
let postedUrl: string | null = null;

registerEndpoint('/v2/Chapters/src-1/DownloadOptions', () => options);
registerEndpoint('/v2/Chapters/src-1/Download', {
    method: 'POST',
    handler: async (event) => {
        postedUrl = ((await readBody(event)) as { url: string }).url;
        return {};
    },
});

describe('DownloadChoiceModal', () => {
    beforeEach(() => {
        options = {
            options: [
                { label: 'Spawn #376 (Empire)', url: 'https://getcomics.org/dls/empire', size: '89 MB' },
                { label: 'Spawn #376', url: 'https://getcomics.org/dls/series', size: '67 MB' },
            ],
            reason: null,
        };
        postedUrl = null;
        document.body.innerHTML = '';
        clearNuxtData();
    });

    it('lists each offered download and queues the picked one', async () => {
        const wrapper = await mountSuspended(DownloadChoiceModal, { props: { sourceKey: 'src-1', open: true } });

        await vi.waitFor(() => expect(document.body.textContent).toContain('Spawn #376 (Empire)'));
        expect(document.body.textContent).toContain('89 MB');
        expect(document.body.textContent).toContain('67 MB');

        const fetchButtons = [...document.body.querySelectorAll('button')].filter((b) => b.textContent?.includes('Fetch'));
        expect(fetchButtons).toHaveLength(2);
        fetchButtons[1]!.click();

        await vi.waitFor(() => expect(postedUrl).toBe('https://getcomics.org/dls/series'));
        wrapper.unmount();
    });

    it('explains a post that truly needs manual handling', async () => {
        options = { options: [], reason: 'only available via MEGA — download manually' };
        const wrapper = await mountSuspended(DownloadChoiceModal, { props: { sourceKey: 'src-1', open: true } });

        await vi.waitFor(() => expect(document.body.textContent).toContain('only available via MEGA'));
        expect([...document.body.querySelectorAll('button')].filter((b) => b.textContent?.includes('Fetch'))).toHaveLength(0);
        wrapper.unmount();
    });
});
