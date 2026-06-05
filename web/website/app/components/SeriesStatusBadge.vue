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

const props = withDefaults(
    defineProps<{
        series: AnySeries;
        /** 'track' = how Kenku handles it; 'release' = publication status. */
        type?: 'track' | 'release';
        size?: 'sm' | 'md' | 'lg';
    }>(),
    { type: 'track', size: 'sm' }
);

const meta = computed(() => (props.type === 'release' ? releaseStatusMeta(props.series) : trackStateMeta(props.series)));
</script>
