<template>
    <span class="flex -space-x-1 shrink-0">
        <span
            v-for="(colour, i) in colours"
            :key="i"
            data-test="swatch"
            class="rounded-full ring-1 ring-inverted/10"
            :class="size === 'lg' ? 'size-6' : 'size-5'"
            :style="{ backgroundColor: colour }" />
    </span>
</template>

<script setup lang="ts">
import type { Seeds } from '~/theme/generate';

const props = withDefaults(defineProps<{ seeds: Seeds; size?: 'sm' | 'lg' }>(), { size: 'sm' });
const colours = computed(() => [props.seeds.primary, props.seeds.secondary, props.seeds.neutral]);
</script>
