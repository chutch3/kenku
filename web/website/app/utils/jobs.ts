import type { components } from '#open-fetch-schemas/api';

type QueuedJob = components['schemas']['QueuedJob'];
type JobStatus = QueuedJob['status'];

/** Friendly verbs instead of handler type names. */
export const JOB_LABELS: Record<string, string> = {
    DownloadChapter: 'Download chapter',
    SyncSeriesChapters: 'Sync chapters',
    DownloadCover: 'Download cover',
    ReconcileVolumeBundle: 'Bundle volume',
    ResolveSeriesVolumes: 'Resolve volumes',
    RefreshExternalMetadata: 'Refresh metadata',
    SendNotifications: 'Send notifications',
    Cleanup: 'Cleanup',
    PlaceChapterFile: 'Place chapter file',
    RefreshLibraries: 'Refresh libraries',
    FinalizeTorrent: 'Finalize torrent',
    VerifyDownloadState: 'Verify downloads',
    MoveData: 'Move files',
};

export const jobLabel = (job: QueuedJob) => JOB_LABELS[job.type] ?? job.type;

export const jobStatusColor = (status: JobStatus) =>
    status === 'Succeeded' ? 'success'
        : status === 'Failed' || status === 'NeedsAttention' ? 'error'
            : status === 'Running' ? 'info' : 'neutral';

const ms = (iso?: string | null) => (iso ? Date.parse(iso) : undefined);

/** Run time: finished − started for completed jobs, or live elapsed since start for a running one. */
export const jobDuration = (job: QueuedJob, now: number): string => {
    const started = ms(job.startedAt);
    if (started === undefined) return '';
    const finished = ms(job.finishedAt);
    if (finished !== undefined) return formatDuration(finished - started);
    if (job.status === 'Running') return formatDuration(now - started);
    return '';
};

export const jobWhen = (job: QueuedJob, now: number): string => {
    if (job.status === 'Running') return 'running';
    const finished = ms(job.finishedAt);
    if (finished !== undefined) return formatRelative(finished, now);
    const created = ms(job.createdAt);
    if (created !== undefined) return formatRelative(created, now);
    return '';
};

/** Full lifecycle on hover: queued → started → finished, plus queue wait when known. */
export const jobTimingTitle = (job: QueuedJob): string => {
    const parts = [`Queued: ${job.createdAt}`];
    if (job.startedAt) {
        parts.push(`Started: ${job.startedAt}`);
        parts.push(`Queue wait: ${formatDuration((ms(job.startedAt) ?? 0) - (ms(job.createdAt) ?? 0))}`);
    }
    if (job.finishedAt) parts.push(`Finished: ${job.finishedAt}`);
    return parts.join('\n');
};

/** A failed chapter download may just need a human pick (multi-option post) — offer the chooser. */
export const canChooseDownload = (job: QueuedJob) =>
    job.type === 'DownloadChapter' && (job.status === 'Failed' || job.status === 'NeedsAttention');

/** "ChapterKey" is pinned server-side by DownloadChapterPayloadTests — not a guess at casing. */
export const chapterChoiceKey = (job: QueuedJob): string | undefined =>
    (JSON.parse(job.payload ?? '{}') as { ChapterKey?: string }).ChapterKey;

/** Stable queue order: newest enqueued first; createdAt never changes, so rows hold their place. */
export const sortJobs = (jobs: QueuedJob[]): QueuedJob[] =>
    [...jobs].sort((a, b) => (ms(b.createdAt) ?? 0) - (ms(a.createdAt) ?? 0) || a.key.localeCompare(b.key));

/** Cap the rendered list, but keep older NeedsAttention rows actionable by appending them. */
export const displayedJobs = (jobs: QueuedJob[], limit = 100): QueuedJob[] => {
    const sorted = sortJobs(jobs);
    const recent = sorted.slice(0, limit);
    const olderAttention = sorted.slice(limit).filter((j) => j.status === 'NeedsAttention');
    return [...recent, ...olderAttention];
};

export const activeJobCount = (jobs: QueuedJob[]) =>
    jobs.filter((j) => j.status === 'Running' || j.status === 'Queued').length;

export const needsAttentionCount = (jobs: QueuedJob[]) =>
    jobs.filter((j) => j.status === 'NeedsAttention').length;
