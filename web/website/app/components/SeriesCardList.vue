<template>
    <div
        class="grid min-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width),_1fr))] max-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width-sm),_1fr))] gap-4">
        <SeriesCard
            v-for="(m, i) in series"
            :key="m.key"
            :series="m"
            :rollup="rollups?.[m.key]"
            :expanded="i === expanded"
            :style="{ '--rev-i': Math.min(i, 24) }"
            class="reveal cursor-pointer rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-primary"
            role="button"
            tabindex="0"
            :aria-label="m.name"
            @click="$emit('click', m)"
            @keydown.enter="$emit('click', m)"
            @keydown.space.prevent="$emit('click', m)" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

const expanded = ref(-1);

defineEmits<{ (e: 'click', series: MinimalSeries | Series): void }>();
defineProps<{ series?: (MinimalSeries | Series)[]; rollups?: Record<string, SeriesRollup> }>();
</script>
