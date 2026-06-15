<template>
    <div class="kenku-lift group relative max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) mt-4 mr-4 rounded-lg">
        <SeriesCover :series="series" blur>
            <template #footer>
                <div class="mt-1.5 flex items-center justify-between gap-2">
                    <span class="font-mono text-[0.65rem] uppercase tracking-wide text-white/80" :title="meta.hint">
                        {{ kind === 'comic' ? 'Comic' : 'Manga' }} · {{ meta.label }}
                    </span>
                    <span v-if="series.sourceIds.length" class="flex items-center gap-0.5 shrink-0">
                        <SourceIcon v-for="m in series.sourceIds.slice(0, 3)" v-bind="m" :key="m.key" :ring="false" class="!m-0 !w-4 !h-4" />
                        <span v-if="series.sourceIds.length > 3" class="font-mono text-[0.6rem] text-white/70">+{{ series.sourceIds.length - 3 }}</span>
                    </span>
                </div>

                <!-- Progress at a glance: no extra fetch, the rollup already carries the counts. -->
                <div v-if="progress" data-test="card-progress" class="mt-1.5 flex items-center gap-1.5">
                    <UProgress
                        :model-value="progress.downloaded"
                        :max="progress.total"
                        size="xs"
                        :color="progress.complete ? 'success' : 'info'"
                        class="grow" />
                    <span class="font-mono text-[0.6rem] text-white/80 tabular-nums shrink-0">{{ progress.downloaded }}/{{ progress.total }}</span>
                </div>

                <!-- Needs attention: show the failure and let the user re-sync without opening the series. -->
                <div v-if="trackState === 'attention'" class="mt-1.5 flex items-center gap-1.5">
                    <span class="text-[0.6rem] text-vermillion-200 truncate" :title="rollup?.lastError ?? meta.hint">
                        {{ rollup?.lastError ?? 'A job needs attention' }}
                    </span>
                    <UButton
                        size="xs"
                        color="error"
                        variant="soft"
                        icon="i-lucide-refresh-ccw"
                        :loading="retrying"
                        class="ml-auto shrink-0"
                        aria-label="Retry sync"
                        @click.stop="retrySync"
                        >Retry</UButton
                    >
                </div>
            </template>
        </SeriesCover>

        <!-- Status, non-color-reliant: an icon badge in the corner pairs with the bottom color bar. -->
        <UBadge
            :color="meta.color"
            :icon="meta.icon"
            variant="solid"
            size="sm"
            class="absolute top-2 right-2 z-10 backdrop-blur-sm"
            :title="meta.hint"
            :aria-label="meta.label" />
        <span data-test="status-bar" class="absolute bottom-0 inset-x-0 h-1 rounded-b-lg z-10" :class="meta.bar" :title="meta.hint" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
import type { PageCardProps } from '#ui/components/PageCard.vue';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

const props = defineProps<SeriesCardProps>();

const { $api } = useNuxtApp();
const toast = useToast();

const { data: connectors } = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, server: false });
const kind = computed(() => seriesKind(props.series, connectors.value));
const trackState = computed(() => seriesTrackState(props.series, props.rollup));
const meta = computed(() => trackStateMeta(props.series, props.rollup));

const progress = computed(() => {
    const r = props.rollup;
    if (!props.series.fileLibraryId || !r || r.wantedChapters <= 0) return null;
    return { downloaded: r.downloadedChapters, total: r.wantedChapters, complete: r.downloadedChapters >= r.wantedChapters };
});

const retrying = ref(false);
const retrySync = async () => {
    if (retrying.value) return;
    retrying.value = true;
    try {
        await $api('/v2/Series/{MangaId}/Sync', { method: 'POST', path: { MangaId: props.series.key } });
        toast.add({ title: 'Sync queued', description: `${props.series.name} will retry from your sources.`, icon: 'i-lucide-cloud-download', color: 'success' });
    } catch {
        toast.add({ title: 'Retry failed', description: 'Could not queue a sync. Try again.', icon: 'i-lucide-triangle-alert', color: 'error' });
    } finally {
        retrying.value = false;
    }
};

export interface SeriesCardProps extends /* @vue-ignore */ PageCardProps {
    series: Series | MinimalSeries;
    rollup?: SeriesRollup | null;
}
</script>
