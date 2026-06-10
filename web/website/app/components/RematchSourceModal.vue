<template>
    <UModal
        v-model:open="open"
        :title="`Re-match ${source.mangaConnectorName} link`"
        description="Pick the entry this series should actually point at — its chapters re-sync immediately.">
        <template #body>
            <div class="flex flex-col gap-3">
                <div class="flex gap-2">
                    <UInput v-model="query" class="grow" icon="i-lucide-search" :disabled="searching" @keydown.enter="performSearch" />
                    <UButton color="primary" icon="i-lucide-search" :loading="searching" :disabled="!query" @click="performSearch">
                        Search
                    </UButton>
                </div>
                <p class="text-xs text-muted">
                    Currently linked to <span class="font-mono text-toned">{{ source.idOnConnectorSite }}</span>
                </p>

                <p v-if="searched && !results.length" class="text-sm text-muted">No matches on {{ source.mangaConnectorName }}.</p>
                <ul v-else class="flex flex-col gap-1.5 max-h-80 overflow-y-auto">
                    <li v-for="r in results" :key="r.key" class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                        <FallbackImage :src="r.coverUrl" :alt="r.name" class="w-10 rounded shrink-0" />
                        <div class="min-w-0 grow">
                            <p class="text-sm truncate text-highlighted">{{ r.name }}</p>
                            <p class="font-mono text-[0.65rem] text-dimmed truncate">{{ r.sourceIds[0]?.idOnConnectorSite }}</p>
                        </div>
                        <UButton size="xs" color="primary" :loading="linking === r.key" @click="rematch(r)">Use this</UButton>
                    </li>
                </ul>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type MinimalSeries = components['schemas']['MinimalSeries'];
type SourceIdDto = components['schemas']['SeriesSourceId'];

const props = defineProps<{ mangaId: string; source: SourceIdDto; seriesName?: string }>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'rematched'): void }>();

const { $api } = useNuxtApp();

const query = ref(props.seriesName ?? '');
const searching = ref(false);
const searched = ref(false);
const results = ref<MinimalSeries[]>([]);
const linking = ref<string | null>(null);

const performSearch = async () => {
    if (!query.value || searching.value) return;
    searching.value = true;
    try {
        results.value =
            (await $api('/v2/Search/{MangaConnectorName}/{Query}', {
                path: { MangaConnectorName: props.source.mangaConnectorName, Query: query.value },
            })) ?? [];
    } catch {
        results.value = [];
    } finally {
        searched.value = true;
        searching.value = false;
    }
};

const rematch = async (result: MinimalSeries) => {
    const target = result.sourceIds[0];
    if (!target) return;
    linking.value = result.key;
    try {
        await $api('/v2/Series/{MangaId}/Source/{SourceIdKey}/Rematch', {
            method: 'POST',
            path: { MangaId: props.mangaId, SourceIdKey: props.source.key },
            body: { idOnConnectorSite: target.idOnConnectorSite, websiteUrl: target.websiteUrl ?? undefined },
        });
        emit('rematched');
        open.value = false;
    } finally {
        linking.value = null;
    }
};
</script>
