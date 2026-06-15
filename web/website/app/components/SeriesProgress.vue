<template>
    <div v-if="total > 0" class="flex flex-col gap-1">
        <div class="flex items-baseline justify-between">
            <span class="text-xs uppercase tracking-wide text-muted">Downloaded</span>
            <span class="font-mono text-xs text-toned">{{ downloaded }} / {{ total }}</span>
        </div>
        <UProgress :model-value="downloaded" :max="total" size="sm" :color="downloaded >= total ? 'success' : 'primary'" />
    </div>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string }>();
const { $api } = useNuxtApp();

// Two cheap count-only queries (pageSize 1) — the API has no per-series count field. Keyed so the
// fetch joins the Nuxt cache: dedup'd across components and refreshable, instead of a raw per-mount call.
const { data } = await useAsyncData(
    `series-progress:${props.mangaId}`,
    async () => {
        const [all, dl] = await Promise.all([
            $api('/v2/Chapters/Series/{MangaId}', { method: 'POST', path: { MangaId: props.mangaId }, query: { page: 1, pageSize: 1 }, body: {} }),
            $api('/v2/Chapters/Series/{MangaId}', { method: 'POST', path: { MangaId: props.mangaId }, query: { page: 1, pageSize: 1 }, body: { downloaded: true } }),
        ]);
        return { total: all.totalCount ?? 0, downloaded: dl.totalCount ?? 0 };
    },
    { server: false, watch: [() => props.mangaId], default: () => ({ total: 0, downloaded: 0 }) }
);

const total = computed(() => data.value?.total ?? 0);
const downloaded = computed(() => data.value?.downloaded ?? 0);
</script>
