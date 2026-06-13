import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { readBody } from 'h3';
import DownloadChoiceModal from '~/components/DownloadChoiceModal.vue';

let options: object = {};
let postedUrl: string | null = null;

registerEndpoint('/v2/Chapters/src-1/DownloadOptions', () => options);
registerEndpoint('/v2/Chapters/src-slow/DownloadOptions', async () => {
    await new Promise((r) => setTimeout(r, 150));
    return { options: [{ label: 'Slow Option', url: 'https://getcomics.org/dls/slow', size: '1 MB' }], reason: null };
});
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

    it('ignores a stale options response after the modal is re-targeted', async () => {
        const wrapper = await mountSuspended(DownloadChoiceModal, { props: { sourceKey: 'src-slow', open: true } });

        // Close mid-fetch and re-open on another chapter's failed job.
        await wrapper.setProps({ open: false });
        await wrapper.setProps({ sourceKey: 'src-1' });
        await wrapper.setProps({ open: true });

        await vi.waitFor(() => expect(document.body.textContent).toContain('Spawn #376 (Empire)'));
        // Let the slow response land; it must not replace the current chapter's options.
        await new Promise((r) => setTimeout(r, 250));
        expect(document.body.textContent).toContain('Spawn #376 (Empire)');
        expect(document.body.textContent).not.toContain('Slow Option');
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
