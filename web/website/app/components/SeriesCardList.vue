<template>
    <div
        class="grid min-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width),_1fr))] max-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width-sm),_1fr))] gap-4">
        <SeriesCard
            v-for="(m, i) in series"
            :key="m.key"
            :series="m"
            :expanded="i === expanded"
            :style="{ '--rev-i': Math.min(i, 24) }"
            class="reveal cursor-pointer"
            @click="$emit('click', m)" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const expanded = ref(-1);

defineEmits<{ (e: 'click', series: MinimalSeries | Series): void }>();
defineProps<{ series?: (MinimalSeries | Series)[] }>();
</script>
