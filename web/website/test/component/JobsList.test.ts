import { describe, it, expect, beforeEach } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import JobsList from '~/components/JobsList.vue';

// The jobs fetch is cached by a fixed key, so reset it between tests to stop data bleeding across them.
beforeEach(() => clearNuxtData());

interface Job {
    key: string;
    type: string;
    name: string;
    state: string;
    createdAt: string;
    startedAt: string | null;
    finishedAt: string | null;
    error: string | null;
}

function job(overrides: Partial<Job>): Job {
    return {
        key: 'k1',
        type: 'DownloadChapterFromSourceWorker',
        name: 'DownloadChapterFromSourceWorker chapter-1',
        state: 'Completed',
        createdAt: '2026-06-06T00:00:00Z',
        startedAt: '2026-06-06T00:00:00Z',
        finishedAt: '2026-06-06T00:00:01Z',
        error: null,
        ...overrides,
    };
}

describe('JobsList', () => {
    it('lists each persisted job with its state', async () => {
        registerEndpoint('/v2/Jobs', () => [
            job({ key: 'a', name: 'Download A', state: 'Completed' }),
            job({ key: 'b', name: 'Download B', state: 'Failed', error: 'boom' }),
        ]);

        const wrapper = await mountSuspended(JobsList);

        expect(wrapper.text()).toContain('Download A');
        expect(wrapper.text()).toContain('Completed');
        expect(wrapper.text()).toContain('Download B');
        expect(wrapper.text()).toContain('Failed');
        expect(wrapper.text()).toContain('boom');
    });

    it('shows an empty state when no jobs are recorded', async () => {
        registerEndpoint('/v2/Jobs', () => []);

        const wrapper = await mountSuspended(JobsList);

        expect(wrapper.text().toLowerCase()).toContain('no jobs recorded');
    });
});
