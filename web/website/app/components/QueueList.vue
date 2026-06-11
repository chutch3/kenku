<template>
    <div class="flex flex-col gap-2">
        <UAlert
            v-if="needsAttentionCount"
            color="error" variant="subtle" icon="i-lucide-triangle-alert"
            :title="`${needsAttentionCount} ${needsAttentionCount === 1 ? 'job needs' : 'jobs need'} attention`"
            description="Review the error, then retry or dismiss." />
        <p v-if="!jobs.length" class="text-sm text-muted">No jobs in the queue.</p>
        <ul v-else class="flex flex-col gap-1">
            <li v-for="job in displayedJobs" :key="job.key" class="flex flex-col text-sm">
                <div class="flex items-center gap-2 cursor-pointer" @click="toggle(job.key)">
                    <UBadge :color="statusColor(job.status)" variant="subtle" class="w-32 justify-center">{{ job.status }}</UBadge>
                    <span class="grow truncate" :title="job.type">
                        {{ jobLabel(job) }}
                        <NuxtLink
                            v-if="seriesName(job)"
                            :to="`/series/${job.resourceKey}`"
                            class="text-secondary hover:underline"
                            @click.stop>{{ seriesName(job) }}</NuxtLink>
                        <span v-if="job.progress" class="text-muted">— {{ job.progress }}</span>
                    </span>
                    <span v-if="job.attempts > 1" class="text-dimmed">×{{ job.attempts }}</span>
                    <span v-if="durationLabel(job)" class="text-dimmed tabular-nums w-16 text-right">{{ durationLabel(job) }}</span>
                    <span v-if="whenLabel(job)" class="text-muted text-xs w-24 text-right">{{ whenLabel(job) }}</span>
                    <span v-if="job.error" class="text-error truncate max-w-80">{{ job.error }}</span>
                    <UButton
                        v-if="job.status === 'NeedsAttention'"
                        size="xs" color="primary" :loading="busy === job.key" @click.stop="retry(job.key)">
                        Retry
                    </UButton>
                    <UButton
                        v-if="job.status === 'NeedsAttention'"
                        size="xs" variant="soft" color="neutral" :loading="busy === job.key" @click.stop="dismiss(job.key)">
                        Dismiss
                    </UButton>
                    <UButton
                        v-if="job.status === 'Queued' || job.status === 'Running'"
                        size="xs" variant="soft" color="error" :loading="busy === job.key" @click.stop="cancel(job.key)">
                        Cancel
                    </UButton>
                    <UIcon :name="expanded.has(job.key) ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'" class="text-dimmed shrink-0" />
                </div>
                <div v-if="expanded.has(job.key)" class="ml-34 my-1 flex flex-col gap-1 text-xs">
                    <p v-if="job.error" class="text-error whitespace-pre-wrap break-words">{{ job.error }}</p>
                    <p class="text-muted">{{ job.type }} · attempt {{ job.attempts }}/{{ job.maxAttempts }}</p>
                    <p class="text-muted whitespace-pre-line">{{ timingTitle(job) }}</p>
                </div>
            </li>
        </ul>
        <p v-if="jobs.length > displayedJobs.length" class="text-xs text-muted">
            Showing {{ displayedJobs.length }} of {{ jobs.length }} jobs, most recent first.
        </p>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type QueuedJob = components['schemas']['QueuedJob'];

const { $api } = useNuxtApp();
const { data, refresh } = await useApi('/v2/JobQueue', { key: FetchKeys.JobQueue.All, server: false });
const jobs = computed<QueuedJob[]>(() => data.value ?? []);
const busy = ref<string | null>(null);

// Make rows readable: a friendly verb instead of the handler type name, the series the job belongs
// to (the resource key is the series for series-scoped jobs), and the recorded outcome.
const JOB_LABELS: Record<string, string> = {
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
const jobLabel = (job: QueuedJob) => JOB_LABELS[job.type] ?? job.type;

const { data: seriesList } = await useApi('/v2/Series', { key: FetchKeys.Series.All, server: false });
const seriesName = (job: QueuedJob) => seriesList.value?.find((s) => s.key === job.resourceKey)?.name;

// A ticking clock so a Running job's elapsed time updates live, and a poll so jobs that flow through
// quickly are actually visible instead of only appearing on manual reload.
const now = ref(Date.now());
let tick: ReturnType<typeof setInterval> | undefined;
let poll: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
    tick = setInterval(() => { now.value = Date.now(); }, 1000);
    poll = setInterval(() => { refresh(); }, 2000);
});
onBeforeUnmount(() => {
    if (tick) clearInterval(tick);
    if (poll) clearInterval(poll);
});

const ms = (iso?: string | null) => (iso ? Date.parse(iso) : undefined);

// Stable queue order: newest enqueued first. createdAt never changes, so a row keeps its place while
// its status flips in place under the 2s poll; the banner above flags anything needing attention.
const sortedJobs = computed(() => [...jobs.value].sort((a, b) =>
    (ms(b.createdAt) ?? 0) - (ms(a.createdAt) ?? 0) || a.key.localeCompare(b.key)));

const needsAttentionCount = computed(() => jobs.value.filter(j => j.status === 'NeedsAttention').length);

// Cap the rendered list: retention bounds the table to a few days, but that can still be hundreds of
// rows. NeedsAttention jobs older than the cap stay actionable — appended after the recent window.
const DISPLAY_LIMIT = 100;
const displayedJobs = computed(() => {
    const recent = sortedJobs.value.slice(0, DISPLAY_LIMIT);
    const olderAttention = sortedJobs.value.slice(DISPLAY_LIMIT).filter((j) => j.status === 'NeedsAttention');
    return [...recent, ...olderAttention];
});

const expanded = ref(new Set<string>());
const toggle = (key: string) => {
    if (!expanded.value.delete(key)) expanded.value.add(key);
    expanded.value = new Set(expanded.value);
};

// Run time: finished − started for completed jobs, or live elapsed since start for a running one.
const durationLabel = (job: QueuedJob) => {
    const started = ms(job.startedAt);
    if (started === undefined) return '';
    const finished = ms(job.finishedAt);
    if (finished !== undefined) return formatDuration(finished - started);
    if (job.status === 'Running') return formatDuration(now.value - started);
    return '';
};

const whenLabel = (job: QueuedJob) => {
    if (job.status === 'Running') return 'running';
    const finished = ms(job.finishedAt);
    if (finished !== undefined) return formatRelative(finished, now.value);
    const created = ms(job.createdAt);
    if (created !== undefined) return formatRelative(created, now.value);
    return '';
};

// Full lifecycle on hover: queued → started → finished, plus queue wait when known.
const timingTitle = (job: QueuedJob) => {
    const parts = [`Queued: ${job.createdAt}`];
    if (job.startedAt) {
        parts.push(`Started: ${job.startedAt}`);
        const wait = (ms(job.startedAt) ?? 0) - (ms(job.createdAt) ?? 0);
        parts.push(`Queue wait: ${formatDuration(wait)}`);
    }
    if (job.finishedAt) parts.push(`Finished: ${job.finishedAt}`);
    return parts.join('\n');
};

const statusColor = (status: QueuedJob['status']) =>
    status === 'Succeeded' ? 'success'
        : status === 'Failed' || status === 'NeedsAttention' ? 'error'
            : status === 'Running' ? 'info' : 'neutral';

const retry = async (jobId: string) => {
    busy.value = jobId;
    try {
        await $api('/v2/JobQueue/{JobId}/Retry', { method: 'POST', path: { JobId: jobId } });
        await refresh();
    } finally {
        busy.value = null;
    }
};

const cancel = async (jobId: string) => {
    busy.value = jobId;
    try {
        await $api('/v2/JobQueue/{JobId}/Cancel', { method: 'POST', path: { JobId: jobId } });
        await refresh();
    } finally {
        busy.value = null;
    }
};

const dismiss = async (jobId: string) => {
    busy.value = jobId;
    try {
        await $api('/v2/JobQueue/{JobId}/Dismiss', { method: 'POST', path: { JobId: jobId } });
        await refresh();
    } finally {
        busy.value = null;
    }
};
</script>
