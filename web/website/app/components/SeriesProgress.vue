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

const total = ref(0);
const downloaded = ref(0);

// Two cheap count-only queries (pageSize 1) — the API has no per-series count field.
const fetchCounts = async () => {
    try {
        const [all, dl] = await Promise.all([
            $api('/v2/Chapters/Series/{MangaId}', {
                method: 'POST',
                path: { MangaId: props.mangaId },
                query: { page: 1, pageSize: 1 },
                body: {},
            }),
            $api('/v2/Chapters/Series/{MangaId}', {
                method: 'POST',
                path: { MangaId: props.mangaId },
                query: { page: 1, pageSize: 1 },
                body: { downloaded: true },
            }),
        ]);
        total.value = all.totalCount ?? 0;
        downloaded.value = dl.totalCount ?? 0;
    } catch {
        /* counts are best-effort */
    }
};

onMounted(fetchCounts);
watch(() => props.mangaId, fetchCounts);
</script>
