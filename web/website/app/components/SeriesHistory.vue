<template>
    <UCard>
        <template #header>
            <SettingsHeader title="History" subtitle="What Kenku has done with this series." />
        </template>
        <p v-if="status === 'pending'" class="text-sm text-muted">Loading…</p>
        <p v-else-if="!events.length" class="text-sm text-muted">Nothing recorded yet.</p>
        <ul v-else class="flex flex-col gap-2">
            <li v-for="e in events" :key="e.key" class="flex items-baseline justify-between gap-3 text-sm">
                <span class="text-highlighted">{{ humanizeAction(e.action) }}</span>
                <span class="text-dimmed text-xs text-nowrap">{{ when(e.performedAt) }}</span>
            </li>
        </ul>
    </UCard>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string }>();
const { $api } = useNuxtApp();

// The per-series slice of the audit trail — the same /v2/Actions log the global Activity page reads,
// scoped here so a series page shows its own story (downloads, metadata updates, moves).
const { data, status } = await useAsyncData(
    `series-history-${props.mangaId}`,
    () => $api('/v2/Actions/Filter', { method: 'POST', body: { mangaId: props.mangaId }, query: { page: 1, pageSize: 25 } }),
    { lazy: true, server: false }
);

const events = computed(() => data.value?.data ?? []);
const humanizeAction = (action?: string) => (action ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');
const when = (iso?: string | null) => (iso ? formatRelative(Date.parse(iso), Date.now()) : '');
</script>
