<template>
    <div class="flex flex-col gap-2">
        <UAlert
            v-if="attentionCount"
            color="error" variant="subtle" icon="i-lucide-triangle-alert"
            :title="`${attentionCount} ${attentionCount === 1 ? 'job needs' : 'jobs need'} attention`"
            description="Review the error, then retry or dismiss." />
        <p v-if="!jobs.length" class="text-sm text-muted">No jobs in the queue.</p>
        <ul v-else class="flex flex-col gap-1">
            <li v-for="job in displayed" :key="job.key" class="flex flex-col text-sm rounded-md border-s-2 ps-2" :class="rowAccent(job)">
                <div
                    class="flex items-center gap-2 cursor-pointer py-1"
                    role="button"
                    tabindex="0"
                    :aria-expanded="expanded.has(job.key)"
                    :aria-label="`${jobLabel(job)} — ${job.status}`"
                    @click="toggle(job.key)"
                    @keydown.enter.self="toggle(job.key)"
                    @keydown.space.self.prevent="toggle(job.key)">
                    <UBadge :color="jobStatusColor(job.status)" variant="subtle" class="w-32 justify-center">{{ job.status }}</UBadge>
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
                    <span v-if="jobDuration(job, now)" class="text-dimmed tabular-nums w-16 text-right">{{ jobDuration(job, now) }}</span>
                    <span v-if="jobWhen(job, now)" class="text-muted text-xs w-24 text-right">{{ jobWhen(job, now) }}</span>
                    <span v-if="job.error" class="text-error truncate max-w-80">{{ job.error }}</span>
                    <UButton
                        v-if="canChooseDownload(job)"
                        size="xs" variant="soft" color="secondary" @click.stop="chooseDownload(job)">
                        Choose download
                    </UButton>
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
                    <p class="text-muted whitespace-pre-line">{{ jobTimingTitle(job) }}</p>
                </div>
            </li>
        </ul>
        <p v-if="jobs.length > displayed.length" class="text-xs text-muted">
            Showing {{ displayed.length }} of {{ jobs.length }} jobs, most recent first.
        </p>
        <DownloadChoiceModal v-if="choiceFor" v-model:open="choiceOpen" :source-key="choiceFor" @queued="refresh()" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type QueuedJob = components['schemas']['QueuedJob'];

const { jobs, displayed, attentionCount, seriesName, now, busy, refresh, retry, cancel, dismiss } = await useJobQueue();

// A failed chapter download may just need a human pick (multi-option post) — offer the chooser.
const choiceFor = ref<string | null>(null);
const choiceOpen = ref(false);
const chooseDownload = (job: QueuedJob) => {
    const key = chapterChoiceKey(job);
    if (!key) return;
    choiceFor.value = key;
    choiceOpen.value = true;
};

// Left accent makes the queue scannable at a glance without leaning on the badge text alone.
const ROW_ACCENT: Record<string, string> = {
    Running: 'border-info',
    NeedsAttention: 'border-error',
    Failed: 'border-error',
    Succeeded: 'border-success/60',
};
const rowAccent = (job: QueuedJob) => ROW_ACCENT[job.status] ?? 'border-transparent';

const expanded = ref(new Set<string>());
const toggle = (key: string) => {
    if (!expanded.value.delete(key)) expanded.value.add(key);
    expanded.value = new Set(expanded.value);
};
</script>
