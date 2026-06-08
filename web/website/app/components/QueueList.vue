<template>
    <div class="flex flex-col gap-2">
        <p v-if="!jobs.length" class="text-sm text-muted">No jobs in the queue.</p>
        <ul v-else class="flex flex-col gap-1">
            <li v-for="job in sortedJobs" :key="job.key" class="flex items-center gap-2 text-sm">
                <UBadge :color="statusColor(job.status)" variant="subtle" class="w-32 justify-center">{{ job.status }}</UBadge>
                <span class="grow truncate" :title="job.type">{{ job.type }}</span>
                <span v-if="job.attempts > 1" class="text-dimmed">×{{ job.attempts }}</span>
                <span
                    v-if="durationLabel(job)"
                    class="text-dimmed tabular-nums w-16 text-right"
                    :title="timingTitle(job)">{{ durationLabel(job) }}</span>
                <span
                    v-if="whenLabel(job)"
                    class="text-muted text-xs w-24 text-right"
                    :title="timingTitle(job)">{{ whenLabel(job) }}</span>
                <span v-if="job.error" class="text-error truncate max-w-80" :title="job.error">{{ job.error }}</span>
                <UButton
                    v-if="job.status === 'NeedsAttention'"
                    size="xs" color="primary" :loading="busy === job.key" @click="retry(job.key)">
                    Retry
                </UButton>
                <UButton
                    v-if="job.status === 'Queued' || job.status === 'Running'"
                    size="xs" variant="soft" color="error" :loading="busy === job.key" @click="cancel(job.key)">
                    Cancel
                </UButton>
            </li>
        </ul>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type QueuedJob = components['schemas']['QueuedJob'];

const { $api } = useNuxtApp();
const { data, refresh } = await useApi('/v2/JobQueue', { key: FetchKeys.JobQueue.All, server: false });
const jobs = computed<QueuedJob[]>(() => data.value ?? []);
const busy = ref<string | null>(null);

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
const activityMs = (job: QueuedJob) => ms(job.finishedAt) ?? ms(job.startedAt) ?? ms(job.createdAt) ?? 0;

// Most-recent activity first, so what just ran (or is running) is at the top of a long queue.
const sortedJobs = computed(() => [...jobs.value].sort((a, b) => activityMs(b) - activityMs(a)));

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
</script>
