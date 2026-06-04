import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { flushPromises } from '@vue/test-utils';
import { readBody } from 'h3';
import MetadataSourceLink from '~/components/MetadataSourceLink.vue';

interface Candidate {
    mangaDexId: string;
    title: string;
    author: string | null;
    chapterCount: number;
    score: number;
    matchReasons: string[];
    externalId: string;
}

// GET metadataSource and PUT metadataSource share a path, so a single handler branches on method.
function registerSource(mangaId: string, status: string, onPut?: (body: unknown) => void) {
    registerEndpoint(`/v2/Series/${mangaId}/metadataSource`, {
        handler: async (event) => {
            if (event.method === 'PUT') {
                onPut?.(await readBody(event));
                return null;
            }
            return { sourceType: 'MangaDex', externalId: null, status, lastSyncedAt: null, matchScore: null };
        },
    });
}

function registerCandidates(mangaId: string, candidates: Candidate[]) {
    registerEndpoint(`/v2/Series/${mangaId}/metadataSource/candidates`, () => candidates);
}

const firePunch: Candidate = {
    mangaDexId: 'uuid-fp',
    title: 'Fire Punch',
    author: 'Tatsuki Fujimoto',
    chapterCount: 83,
    score: 0.98,
    matchReasons: ['Title is very similar', 'Author matches'],
    externalId: 'uuid-fp',
};

describe('MetadataSourceLink', () => {
    it('surfaces an unmatched series as needing a link', async () => {
        registerSource('m-status', 'NoMatch');

        const wrapper = await mountSuspended(MetadataSourceLink, { props: { mangaId: 'm-status', seriesName: 'Fire Punch' } });
        await flushPromises();

        expect(wrapper.text().toLowerCase()).toContain('not matched');
    });

    it('lists scored candidates with their match reasons after searching', async () => {
        registerSource('m-search', 'NoMatch');
        registerCandidates('m-search', [firePunch]);

        const wrapper = await mountSuspended(MetadataSourceLink, { props: { mangaId: 'm-search', seriesName: 'Fire Punch' } });
        await flushPromises();

        await wrapper.find('[data-test="source-search-btn"]').trigger('click');
        await vi.waitFor(() => expect(wrapper.text()).toContain('Fire Punch'));

        expect(wrapper.text()).toContain('Author matches');
        expect(wrapper.text()).toContain('98%');
    });

    it('confirms the chosen candidate via PUT and triggers a refresh', async () => {
        let putBody: unknown = null;
        let refreshed = false;
        registerSource('m-link', 'NoMatch', (b) => (putBody = b));
        registerCandidates('m-link', [firePunch]);
        registerEndpoint(`/v2/Series/m-link/metadataSource/refresh`, {
            method: 'POST',
            handler: () => {
                refreshed = true;
                return { jobId: 'job-1' };
            },
        });

        const wrapper = await mountSuspended(MetadataSourceLink, { props: { mangaId: 'm-link', seriesName: 'Fire Punch' } });
        await flushPromises();
        await wrapper.find('[data-test="source-search-btn"]').trigger('click');
        await vi.waitFor(() => expect(wrapper.find('[data-test="source-link-btn"]').exists()).toBe(true));
        await wrapper.find('[data-test="source-link-btn"]').trigger('click');
        await vi.waitFor(() => expect(refreshed).toBe(true));

        // Confirms the exact MangaDex id the user picked, then kicks off re-resolution.
        expect(putBody).toEqual({ sourceType: 'MangaDex', externalId: 'uuid-fp' });
    });
});
