<template>
    <UCard v-if="torrents?.length">
        <template #header>
            <div>
                <h2 class="font-display text-lg font-semibold text-highlighted">Torrents</h2>
                <p class="text-xs text-muted">What the download client is moving right now.</p>
            </div>
        </template>
        <div class="flex flex-col gap-2">
            <div v-for="t in torrents" :key="t.tag ?? t.name ?? ''" class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                <UIcon :name="stateIcon(t.state)" :class="stateClass(t.state)" class="shrink-0" />
                <div class="min-w-0 grow">
                    <p class="text-sm truncate">{{ t.name }}</p>
                    <p v-if="t.error" class="text-xs text-error truncate">{{ t.error }}</p>
                </div>
                <span class="font-mono text-xs text-muted shrink-0">{{ t.seeders }} seeders</span>
                <span class="font-mono text-xs shrink-0" :class="stateClass(t.state)">
                    {{ Math.round((t.progress ?? 0) * 100) }}%
                </span>
            </div>
        </div>
    </UCard>
</template>

<script setup lang="ts">
const { data: torrents } = useApi('/v2/Torrents', { key: FetchKeys.Torrents, lazy: true, server: false });

const stateIcon = (state?: string | null) =>
    state === 'completed' ? 'i-lucide-check-circle' : state === 'errored' ? 'i-lucide-triangle-alert' : 'i-lucide-arrow-down-circle';
const stateClass = (state?: string | null) =>
    state === 'completed' ? 'text-jade-500' : state === 'errored' ? 'text-error' : 'text-sky-500';

// Live progress: poll while anything is still moving.
const interval = ref<ReturnType<typeof setInterval>>();
onMounted(() => {
    interval.value = setInterval(() => {
        if (torrents.value?.some((t) => t.state === 'downloading')) refreshNuxtData(FetchKeys.Torrents);
    }, 5000);
});
onUnmounted(() => clearInterval(interval.value));
</script>
