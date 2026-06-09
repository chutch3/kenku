<template>
    <UTooltip :text="meta.hint">
        <UBadge :color="meta.color" :icon="meta.icon" :size="size" variant="subtle" class="backdrop-blur-sm">
            {{ meta.label }}
        </UBadge>
    </UTooltip>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];

const props = withDefaults(
    defineProps<{
        series: AnySeries;
        /** 'track' = how Kenku handles it; 'release' = publication status. */
        type?: 'track' | 'release';
        size?: 'sm' | 'md' | 'lg';
        /** Operational rollup for this series; makes the track state reflect actual work. */
        rollup?: SeriesRollup | null;
    }>(),
    { type: 'track', size: 'sm', rollup: null }
);

const meta = computed(() => (props.type === 'release' ? releaseStatusMeta(props.series) : trackStateMeta(props.series, props.rollup)));
</script>
