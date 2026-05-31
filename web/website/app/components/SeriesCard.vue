<template>
    <UCard
        :ui="{ body: 'p-0 sm:p-0', root: 'overflow-visible' }"
        class="relative max-sm:w-[var(--mangacover-width-sm)] max-sm:h-[var(--mangacover-height-sm)] w-(--mangacover-width) h-(--mangacover-height) mt-4 mr-4">
        <SeriesCover :series="series" blur />
        <div class="absolute -top-4 -right-4 flex flex-col bg-pink rounded-full">
            <SourceIcon v-for="m in series.sourceIds" v-bind="m" :key="m.key" />
        </div>
    </UCard>
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
