import { describe, it, expect, beforeEach, vi } from 'vitest';
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
    payload: string;
}

function job(overrides: Partial<QueuedJob>): QueuedJob {
    return {
        key: 'k1', type: 'DownloadChapter', status: 'Queued', attempts: 1, maxAttempts: 5,
        resourceKey: 'series-1', error: null,
        createdAt: '2026-06-06T00:00:00Z', scheduledFor: '2026-06-06T00:00:00Z',
        startedAt: null, finishedAt: null, progress: null,
        payload: '{"ChapterKey":"src-1"}',
        ...overrides,
    };
}

// The queue fetch is cached by a fixed key, so reset it between tests.
beforeEach(() => clearNuxtData());

describe('QueueList', () => {
    it('offers a download chooser on failed chapter downloads only', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'a', type: 'DownloadChapter', status: 'NeedsAttention', error: 'the post offers 2 downloads — choose one from the failed job in Activity' }),
            job({ key: 'b', type: 'ResolveSeriesVolumes', status: 'NeedsAttention', error: 'boom' }),
        ]);
        const wrapper = await mountSuspended(QueueList);
        await vi.waitFor(() => expect(wrapper.text()).toContain('NeedsAttention'));

        const chooseButtons = wrapper.findAll('button').filter((b) => b.text().includes('Choose download'));
        expect(chooseButtons).toHaveLength(1);
    });

    it('lists each runtime job with its status', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'a', type: 'DownloadChapter', status: 'Running' }),
            job({ key: 'b', type: 'ResolveSeriesVolumes', status: 'NeedsAttention', error: 'boom' }),
        ]);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text()).toContain('Download chapter');
        expect(wrapper.text()).toContain('Running');
        expect(wrapper.text()).toContain('Resolve volumes');
        expect(wrapper.text()).toContain('NeedsAttention');
        expect(wrapper.text()).toContain('boom');
    });

    it('shows the series and the recorded outcome on a job row', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 's', type: 'SyncSeriesChapters', status: 'Succeeded', resourceKey: 'series-1',
                  progress: 'connector reported 22 chapters (3 new)', finishedAt: '2026-06-06T00:01:00Z' }),
        ]);
        registerEndpoint('/v2/Series', () => [{
            key: 'series-1', name: 'Berserk', description: '', releaseStatus: 'Continuing',
            sourceIds: [], fileLibraryId: 'lib1', originalLanguage: 'en', coverUrl: '',
        }]);

        const wrapper = await mountSuspended(QueueList);

        await vi.waitFor(() => {
            expect(wrapper.text()).toContain('Sync chapters');
            expect(wrapper.text()).toContain('Berserk');
            expect(wrapper.text()).toContain('22 chapters');
        });
    });

    it('retries a NeedsAttention job via the runtime', async () => {
        registerEndpoint('/v2/JobQueue', () => [job({ key: 'needs', status: 'NeedsAttention' })]);
        let retried = false;
        registerEndpoint('/v2/JobQueue/needs/Retry', { method: 'POST', handler: () => { retried = true; return {}; } });

        const wrapper = await mountSuspended(QueueList);
        const retryButton = wrapper.findAll('button').find((b) => b.text().includes('Retry'));
        await retryButton!.trigger('click');
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

    it('keeps queue order stable by enqueue time, regardless of status or activity', async () => {
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'oldest', status: 'NeedsAttention', error: 'boom', createdAt: '2026-06-06T00:00:00Z' }),
            job({ key: 'middle', status: 'Running', createdAt: '2026-06-06T00:01:00Z', startedAt: '2026-06-06T00:06:00Z' }),
            job({ key: 'newest', status: 'Succeeded', createdAt: '2026-06-06T00:02:00Z', finishedAt: '2026-06-06T00:05:00Z' }),
        ]);

        const wrapper = await mountSuspended(QueueList);

        // Newest enqueued first; an attention or recently-active job changes in place, it doesn't jump.
        const rows = wrapper.findAll('li').map((li) => li.text());
        expect(rows.findIndex((t) => t.includes('Succeeded'))).toBeLessThan(rows.findIndex((t) => t.includes('Running')));
        expect(rows.findIndex((t) => t.includes('Running'))).toBeLessThan(rows.findIndex((t) => t.includes('NeedsAttention')));
    });

    it('expands a row to the full error and lifecycle, collapsed by default', async () => {
        const longError = 'chapter list request failed: HTTP 429 — the indexer is rate limiting; retry after the cooldown elapses';
        registerEndpoint('/v2/JobQueue', () => [
            job({ key: 'needs', status: 'NeedsAttention', error: longError, startedAt: '2026-06-06T00:00:30Z' }),
        ]);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text()).not.toContain('Queued:');

        await wrapper.find('li > div').trigger('click');

        expect(wrapper.text()).toContain('Queued:');
        const fullError = wrapper.findAll('p').find((p) => p.text() === longError);
        expect(fullError, 'untruncated error paragraph').toBeTruthy();
        expect(fullError!.classes()).not.toContain('truncate');
    });

    it('counts appended old attention rows in the footer, not just the recent window', async () => {
        const recent = Array.from({ length: 115 }, (_, i) =>
            job({ key: `r${i}`, status: 'Succeeded', createdAt: '2026-06-06T01:00:00Z' }));
        const oldAttention = Array.from({ length: 5 }, (_, i) =>
            job({ key: `a${i}`, status: 'NeedsAttention', error: 'boom', createdAt: '2026-06-01T00:00:00Z' }));
        registerEndpoint('/v2/JobQueue', () => [...recent, ...oldAttention]);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.findAll('li')).toHaveLength(105);
        expect(wrapper.text()).toContain('Showing 105 of 120');
    });

    it('shows an empty state when the queue is empty', async () => {
        registerEndpoint('/v2/JobQueue', () => []);

        const wrapper = await mountSuspended(QueueList);

        expect(wrapper.text().toLowerCase()).toContain('no jobs in the queue');
    });
});
