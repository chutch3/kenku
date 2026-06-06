<template>
    <div class="flex flex-col gap-2">
        <p v-if="!jobs.length" class="text-sm text-muted">No jobs in the queue.</p>
        <ul v-else class="flex flex-col gap-1">
            <li v-for="job in jobs" :key="job.key" class="flex items-center gap-2 text-sm">
                <UBadge :color="statusColor(job.status)" variant="subtle" class="w-32 justify-center">{{ job.status }}</UBadge>
                <span class="grow truncate" :title="job.type">{{ job.type }}</span>
                <span v-if="job.attempts > 1" class="text-dimmed">×{{ job.attempts }}</span>
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
