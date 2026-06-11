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
            </template>
        </SeriesCover>
        <!-- Track-state at a glance: the cover's bottom edge carries the status color. -->
        <span data-test="status-bar" class="absolute bottom-0 inset-x-0 h-1 rounded-b-lg z-10" :class="barClass" :title="meta.hint" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
import type { PageCardProps } from '#ui/components/PageCard.vue';
import type { TrackState } from '~/composables/useSeriesStatus';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

const props = defineProps<SeriesCardProps>();

const { data: connectors } = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, server: false });
const kind = computed(() => seriesKind(props.series, connectors.value));
const meta = computed(() => trackStateMeta(props.series, props.rollup));

const BAR_CLASSES: Record<TrackState, string> = {
    untracked: 'bg-sumi-400/60',
    paused: 'bg-sumi-400/60',
    attention: 'bg-vermillion-500',
    downloading: 'bg-sky-500',
    upToDate: 'bg-jade-500',
};
const barClass = computed(() => BAR_CLASSES[seriesTrackState(props.series, props.rollup)]);

export interface SeriesCardProps extends /* @vue-ignore */ PageCardProps {
    series: Series | MinimalSeries;
    rollup?: SeriesRollup | null;
}
</script>
