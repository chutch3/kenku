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
                    class="flex flex-col gap-2 rounded-lg p-2 ring-1 text-left transition"
                    :class="t.id === current ? 'ring-primary bg-elevated' : 'ring-default hover:bg-elevated/60'"
                    @click="set(t.id)">
                    <ThemePreview :theme-id="t.id" class="w-full" />
                    <span class="flex items-center gap-1 w-full">
                        <span class="min-w-0 flex-1">
                            <span class="block text-sm text-highlighted truncate">{{ t.name }}</span>
                            <span v-if="t.jaName" class="block text-xs text-dimmed truncate">{{ t.jaName }}</span>
                        </span>
                        <UIcon v-if="t.id === current" name="i-lucide-check" class="text-primary shrink-0" />
                    </span>
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
