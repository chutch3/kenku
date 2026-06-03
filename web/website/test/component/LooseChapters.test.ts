import { describe, it, expect } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { flushPromises } from '@vue/test-utils';
import { readBody } from 'h3';
import LooseChapters from '~/components/LooseChapters.vue';

interface LooseEntry {
    chapterId: string;
    chapterNumber: string;
    fileName: string | null;
    fileExistsOnDisk: boolean;
    isBundled: boolean;
    metadataConfidence: string | null;
}

// Each test uses a distinct mangaId so the volumes endpoint (cached by URL via useAsyncData) doesn't
// bleed between tests.
function registerVolumes(mangaId: string, unassigned: LooseEntry[]) {
    registerEndpoint(`/v2/Series/${mangaId}/volumes`, () => ({
        filesNeedReorganizing: 0,
        layout: 'VolumeCBZ',
        volumes: [],
        unassigned,
    }));
}

function entry(chapterNumber: string): LooseEntry {
    return {
        chapterId: `k${chapterNumber}`,
        chapterNumber,
        fileName: `Ch.${chapterNumber}.cbz`,
        fileExistsOnDisk: true,
        isBundled: false,
        metadataConfidence: null,
    };
}

describe('LooseChapters', () => {
    it('lists each loose chapter from the volumes endpoint', async () => {
        registerVolumes('manga-list', [entry('5'), entry('6')]);

        const wrapper = await mountSuspended(LooseChapters, { props: { mangaId: 'manga-list' } });

        expect(wrapper.text()).toContain('Ch. 5');
        expect(wrapper.text()).toContain('Ch. 6');
    });

    it('shows an empty state when there are no loose chapters', async () => {
        registerVolumes('manga-empty', []);

        const wrapper = await mountSuspended(LooseChapters, { props: { mangaId: 'manga-empty' } });

        expect(wrapper.text().toLowerCase()).toContain('no loose chapters');
    });

    it('posts the chapter→volume assignment when the user assigns a volume', async () => {
        registerVolumes('manga-assign', [entry('5')]);
        let captured: unknown = null;
        registerEndpoint(`/v2/Series/manga-assign/volumes/assignments`, {
            method: 'POST',
            handler: async (event) => {
                captured = await readBody(event);
                return { applied: 1, notFound: [] };
            },
        });

        const wrapper = await mountSuspended(LooseChapters, { props: { mangaId: 'manga-assign' } });

        await wrapper.find('input').setValue('3');
        await wrapper.find('button').trigger('click');
        await flushPromises();

        // Sends volume as a number keyed by chapter number — matching BulkAssignmentRecord.
        expect(captured).toEqual({ assignments: { '5': 3 } });
    });
});
