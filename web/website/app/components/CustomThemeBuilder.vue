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
            <ThemeSwatches :seeds="seeds" size="lg" />
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
import { getTheme } from '~/theme/registry';
import { CUSTOM_THEME_ID } from '~/composables/useTheme';

const { current, customSeeds, setCustom } = useTheme();

// Seed the builder from the saved custom theme, falling back to the default (Karasu) palette.
const defaults = getTheme('karasu')!.seeds;
const seeds = reactive({
    primary: customSeeds.value?.primary ?? defaults.primary,
    secondary: customSeeds.value?.secondary ?? defaults.secondary,
    neutral: customSeeds.value?.neutral ?? defaults.neutral,
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
