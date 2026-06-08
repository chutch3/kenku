import { describe, it, expect, beforeEach } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { flushPromises } from '@vue/test-utils';
import QueueList from '~/components/QueueList.vue';

interface QueuedJob {
    key: string;
    type: string;
    status: string;
    attempts: number;
    maxAttempts: number;
    resourceKey: string | null;
    error: string | null;
    createdAt: string;
    scheduledFor: string;
    startedAt: string | null;
    finishedAt: string | null;
    progress: string | null;
}

function job(overrides: Partial<QueuedJob>): QueuedJob {
    return {
        key: 'k1', type: 'DownloadChapter', status: 'Queued', attempts: 1, maxAttempts: 5,
        resourceKey: 'series-1', error: null,
        createdAt: '2026-06-06T00:00:00Z', scheduledFor: '2026-06-06T00:00:00Z',
        startedAt: null, finishedAt: null, progress: null,
        ...overrides,
    };
}

// The queue fetch is cached by a fixed key, so reset it between tests.
beforeEach(() => clearNuxtData());

describe('QueueList', () => {
    it('lists each runtime job with its status', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'a', type: 'DownloadChapter', status: 'Running' }),
            job({ key: 'b', type: 'ResolveSeriesVolumes', status: 'NeedsAttention', error: 'boom' }),
        ]);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text()).toContain('DownloadChapter');
        expect(wrapper.text()).toContain('Running');
        expect(wrapper.text()).toContain('ResolveSeriesVolumes');
        expect(wrapper.text()).toContain('NeedsAttention');
        expect(wrapper.text()).toContain('boom');
    });

    it('retries a NeedsAttention job via the runtime', async () => {
        registerEndpoint('/v2/JobQueue', () => [job({ key: 'needs', status: 'NeedsAttention' })]);
        let retried = false;
        registerEndpoint('/v2/JobQueue/needs/Retry', { method: 'POST', handler: () => { retried = true; return {}; } });

        const wrapper = await mountSuspended(QueueList);
        await wrapper.find('button').trigger('click');
        await flushPromises();

        expect(retried).toBe(true);
    });

    it('surfaces a banner when jobs need attention', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'a', status: 'Succeeded' }),
            job({ key: 'b', status: 'NeedsAttention', error: 'boom' }),
        ]);

        const wrapper = await mountSuspended(QueueList);

        // The banner is the surfaced affordance — assert its unique copy, not the status badge text.
        expect(wrapper.text().toLowerCase()).toContain('review the error');
    });

    it('dismisses a NeedsAttention job via the runtime', async () => {
        registerEndpoint('/v2/JobQueue', () => [job({ key: 'needs', status: 'NeedsAttention' })]);
        let dismissed = false;
        registerEndpoint('/v2/JobQueue/needs/Dismiss', { method: 'POST', handler: () => { dismissed = true; return {}; } });

        const wrapper = await mountSuspended(QueueList);
        const dismissBtn = wrapper.findAll('button').find(b => b.text() === 'Dismiss');
        await dismissBtn!.trigger('click');
        await flushPromises();

        expect(dismissed).toBe(true);
    });

    it('shows how long a finished job took to run', async () => {
        registerEndpoint('/v2/JobQueue', () => [job({
            key: 'done', status: 'Succeeded',
            startedAt: '2026-06-06T00:00:00Z', finishedAt: '2026-06-06T00:00:01.5Z',
        })]);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text()).toContain('1.5s');
    });

    it('caps the list to the most recent jobs and notes the total', async () => {
        const many = Array.from({ length: 120 }, (_, i) => job({ key: `k${i}`, status: 'Succeeded' }));
        registerEndpoint('/v2/JobQueue', () => many);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.findAll('li')).toHaveLength(100);
        expect(wrapper.text()).toContain('120');
    });

    it('shows an empty state when the queue is empty', async () => {
        registerEndpoint('/v2/JobQueue', () => []);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text().toLowerCase()).toContain('no jobs in the queue');
    });
});
