<template>
    <div class="flex flex-col gap-4">
        <div class="grid grid-cols-3 gap-3">
            <label v-for="f in fields" :key="f.key" class="flex flex-col gap-1 text-sm">
                <span class="text-muted">{{ f.label }}</span>
                <input
                    v-model="seeds[f.key]"
                    type="color"
                    :data-test="`seed-${f.key}`"
                    :aria-label="`${f.label} colour`"
                    class="h-9 w-full rounded cursor-pointer bg-transparent" />
            </label>
        </div>

        <div class="flex items-center gap-2">
            <span class="text-sm text-muted">Preview</span>
            <span class="flex -space-x-1">
                <span class="size-6 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: seeds.primary }" />
                <span class="size-6 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: seeds.secondary }" />
                <span class="size-6 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: seeds.neutral }" />
            </span>
        </div>

        <p v-if="lowContrast" data-test="contrast-warning" class="text-xs text-warning flex items-center gap-1">
            <UIcon name="i-lucide-triangle-alert" />
            The primary colour may be hard to read as text on a light background.
        </p>

        <UButton data-test="apply-custom" color="primary" icon="i-lucide-check" class="w-fit" @click="apply">
            {{ active ? 'Custom theme active' : 'Use this custom theme' }}
        </UButton>
    </div>
</template>

<script setup lang="ts">
import { contrastRatio } from '~/theme/generate';
import { CUSTOM_THEME_ID } from '~/composables/useTheme';

const { current, customSeeds, setCustom } = useTheme();

const seeds = reactive({
    primary: customSeeds.value?.primary ?? '#e5483a',
    secondary: customSeeds.value?.secondary ?? '#12b299',
    neutral: customSeeds.value?.neutral ?? '#585d70',
});

const fields = [
    { key: 'primary', label: 'Primary' },
    { key: 'secondary', label: 'Secondary' },
    { key: 'neutral', label: 'Neutral' },
] as const;

// Pale accents fall below the ~3:1 large-text threshold against a light surface — warn before applying.
const lowContrast = computed(() => contrastRatio(seeds.primary, '#ffffff') < 3);
const active = computed(() => current.value === CUSTOM_THEME_ID);
const apply = () => setCustom({ ...seeds });
</script>
