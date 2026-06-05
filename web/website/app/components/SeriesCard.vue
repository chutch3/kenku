<template>
    <div class="kenku-lift group relative max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) mt-4 mr-4 rounded-lg">
        <SeriesCover :series="series" blur />
        <div
            class="absolute -top-3 -right-3 flex flex-col items-center bg-primary rounded-full p-0.5 shadow-lg shadow-vermillion-900/40 ring-2 ring-default">
            <SourceIcon v-for="m in series.sourceIds" v-bind="m" :key="m.key" />
        </div>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
import type { PageCardProps } from '#ui/components/PageCard.vue';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];

defineProps<SeriesCardProps>();

export interface SeriesCardProps extends /* @vue-ignore */ PageCardProps {
    series: Series | MinimalSeries;
}
</script>
