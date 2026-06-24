<template>
    <div class="flex flex-col gap-5">
        <div v-for="group in grouped" :key="group.package" class="flex flex-col gap-2">
            <p class="text-sm font-medium text-muted">{{ group.package }}</p>
            <div class="grid grid-cols-2 sm:grid-cols-3 gap-2">
                <button
                    v-for="t in group.themes"
                    :key="t.id"
                    type="button"
                    :data-test="`theme-${t.id}`"
                    :aria-pressed="t.id === current"
                    class="flex items-center gap-3 rounded-lg p-2 ring-1 text-left transition"
                    :class="t.id === current ? 'ring-primary bg-elevated' : 'ring-default hover:bg-elevated/60'"
                    @click="set(t.id)">
                    <span class="flex -space-x-1 shrink-0">
                        <span class="size-5 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: t.seeds.primary }" />
                        <span class="size-5 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: t.seeds.secondary }" />
                        <span class="size-5 rounded-full ring-1 ring-inverted/10" :style="{ backgroundColor: t.seeds.neutral }" />
                    </span>
                    <span class="min-w-0">
                        <span class="block text-sm text-highlighted truncate">{{ t.name }}</span>
                        <span v-if="t.jaName" class="block text-xs text-dimmed truncate">{{ t.jaName }}</span>
                    </span>
                    <UIcon v-if="t.id === current" name="i-lucide-check" class="ml-auto text-primary shrink-0" />
                </button>
            </div>
        </div>
    </div>
</template>

<script setup lang="ts">
import type { Theme } from '~/theme/registry';

const { current, themes, set } = useTheme();

const grouped = computed(() => {
    const byPackage = new Map<string, Theme[]>();
    for (const t of themes) {
        const list = byPackage.get(t.package) ?? [];
        list.push(t);
        byPackage.set(t.package, list);
    }
    return [...byPackage.entries()].map(([pkg, group]) => ({ package: pkg, themes: group }));
});
</script>
