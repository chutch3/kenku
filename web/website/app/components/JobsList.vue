<template>
    <div class="flex flex-col gap-2">
        <p v-if="!jobs.length" class="text-sm text-muted">No jobs recorded yet.</p>
        <ul v-else class="flex flex-col gap-1">
            <li v-for="job in jobs" :key="job.key" class="flex items-center gap-2 text-sm">
                <UBadge :color="stateColor(job.state)" variant="subtle" class="w-24 justify-center">{{ job.state }}</UBadge>
                <span class="grow truncate" :title="job.name">{{ job.name }}</span>
                <span v-if="job.error" class="text-error truncate max-w-80" :title="job.error">{{ job.error }}</span>
                <span class="text-dimmed text-nowrap">{{ when(job) }}</span>
            </li>
        </ul>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Job = components['schemas']['Job'];

const { data } = await useApi('/v2/Jobs', { key: FetchKeys.Jobs.All, server: false });

const jobs = computed<Job[]>(() => data.value ?? []);

// Map the worker execution state onto a @nuxt/ui semantic colour.
const stateColor = (state: Job['state']) =>
    state === 'Completed' ? 'success' : state === 'Failed' ? 'error' : state === 'Running' ? 'info' : 'neutral';

const when = (job: Job) =>
    job.finishedAt ? new Date(job.finishedAt).toLocaleString() : new Date(job.createdAt).toLocaleString();
</script>
