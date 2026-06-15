import { describe, it, expect } from 'vitest';
import {
    jobLabel, jobStatusColor, jobDuration, jobWhen, jobTimingTitle, canChooseDownload, chapterChoiceKey,
    sortJobs, displayedJobs, activeJobCount, needsAttentionCount,
} from '~/utils/jobs';
import type { components } from '#open-fetch-schemas/api';

type QueuedJob = components['schemas']['QueuedJob'];

function job(overrides: Partial<QueuedJob> = {}): QueuedJob {
    return {
        key: 'k1', type: 'DownloadChapter', status: 'Queued', attempts: 1, maxAttempts: 5,
        resourceKey: 'series-1', error: null, createdAt: '2026-06-06T00:00:00Z', scheduledFor: '2026-06-06T00:00:00Z',
        startedAt: null, finishedAt: null, progress: null, payload: '{"ChapterKey":"src-1"}', ...overrides,
    } as QueuedJob;
}

describe('jobs util', () => {
    it('maps handler types to friendly labels, passing unknowns through', () => {
        expect(jobLabel(job({ type: 'SyncSeriesChapters' }))).toBe('Sync chapters');
        expect(jobLabel(job({ type: 'SomethingNew' }))).toBe('SomethingNew');
    });

    it('colors statuses by severity', () => {
        expect(jobStatusColor('Succeeded')).toBe('success');
        expect(jobStatusColor('NeedsAttention')).toBe('error');
        expect(jobStatusColor('Failed')).toBe('error');
        expect(jobStatusColor('Running')).toBe('info');
        expect(jobStatusColor('Queued')).toBe('neutral');
    });

    it('reports run time for finished and live for running jobs', () => {
        expect(jobDuration(job({ startedAt: '2026-06-06T00:00:00Z', finishedAt: '2026-06-06T00:00:01.5Z' }), 0)).toBe('1.5s');
        const start = Date.parse('2026-06-06T00:00:00Z');
        expect(jobDuration(job({ status: 'Running', startedAt: '2026-06-06T00:00:00Z' }), start + 3000)).toBe('3.0s');
        expect(jobDuration(job({ startedAt: null }), 0)).toBe('');
    });

    it('labels when: running, finished-relative, or created-relative', () => {
        expect(jobWhen(job({ status: 'Running' }), 0)).toBe('running');
        const t = Date.parse('2026-06-06T00:00:00Z');
        expect(jobWhen(job({ status: 'Succeeded', finishedAt: '2026-06-06T00:00:00Z' }), t + 120000)).toBe('2m ago');
    });

    it('builds a lifecycle timing title with queue wait', () => {
        const title = jobTimingTitle(job({ startedAt: '2026-06-06T00:00:05Z', finishedAt: '2026-06-06T00:00:10Z' }));
        expect(title).toContain('Queued:');
        expect(title).toContain('Queue wait: 5.0s');
        expect(title).toContain('Finished:');
    });

    it('offers the chooser only for failed chapter downloads, and reads its key', () => {
        expect(canChooseDownload(job({ type: 'DownloadChapter', status: 'NeedsAttention' }))).toBe(true);
        expect(canChooseDownload(job({ type: 'DownloadChapter', status: 'Running' }))).toBe(false);
        expect(canChooseDownload(job({ type: 'ResolveSeriesVolumes', status: 'NeedsAttention' }))).toBe(false);
        expect(chapterChoiceKey(job({ payload: '{"ChapterKey":"abc"}' }))).toBe('abc');
        expect(chapterChoiceKey(job({ payload: '{}' }))).toBeUndefined();
    });

    it('sorts newest-enqueued first, key breaking ties', () => {
        const order = sortJobs([
            job({ key: 'old', createdAt: '2026-06-06T00:00:00Z' }),
            job({ key: 'new', createdAt: '2026-06-06T00:02:00Z' }),
        ]).map((j) => j.key);
        expect(order).toEqual(['new', 'old']);
    });

    it('caps the display but appends older needs-attention rows', () => {
        const recent = Array.from({ length: 115 }, (_, i) => job({ key: `r${i}`, status: 'Succeeded', createdAt: '2026-06-06T01:00:00Z' }));
        const oldAttention = Array.from({ length: 5 }, (_, i) => job({ key: `a${i}`, status: 'NeedsAttention', createdAt: '2026-06-01T00:00:00Z' }));
        expect(displayedJobs([...recent, ...oldAttention])).toHaveLength(105);
    });

    it('counts active and needs-attention jobs', () => {
        const list = [job({ status: 'Running' }), job({ status: 'Queued' }), job({ status: 'NeedsAttention' }), job({ status: 'Succeeded' })];
        expect(activeJobCount(list)).toBe(2);
        expect(needsAttentionCount(list)).toBe(1);
    });
});
