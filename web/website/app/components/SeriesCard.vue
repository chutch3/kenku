<template>
    <div class="kenku-lift group relative max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) mt-4 mr-4 rounded-lg">
        <SeriesCover :series="series" blur />
        <div class="absolute top-2 left-2 z-10 max-sm:hidden">
            <SeriesStatusBadge :series="series" :rollup="rollup" />
        </div>
        <div
            v-if="series.sourceIds.length"
            class="absolute top-2 right-2 z-10 flex items-center gap-0.5 rounded-full bg-default/85 backdrop-blur-sm pl-0.5 pr-1.5 py-0.5 ring-1 ring-default shadow-sm">
            <SourceIcon v-for="m in series.sourceIds.slice(0, 3)" v-bind="m" :key="m.key" :ring="false" class="!m-0 !w-5 !h-5" />
            <span v-if="series.sourceIds.length > 3" class="font-mono text-[0.6rem] text-muted">+{{ series.sourceIds.length - 3 }}</span>
        </div>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
import type { PageCardProps } from '#ui/components/PageCard.vue';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

defineProps<SeriesCardProps>();

export interface SeriesCardProps extends /* @vue-ignore */ PageCardProps {
    series: Series | MinimalSeries;
    rollup?: SeriesRollup | null;
}
</script>
