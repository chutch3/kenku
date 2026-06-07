<template>
    <UModal v-model:open="open" :ui="{ content: 'sm:max-w-xl' }" aria-label="Command palette">
        <template #content>
            <UCommandPalette
                :groups="groups"
                :loading="status === 'pending'"
                placeholder="Search your library or jump to a section…"
                class="h-96"
                @update:model-value="onSelect" />
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { CommandPaletteItem, CommandPaletteGroup } from '@nuxt/ui';

const open = useState('cmdk-open', () => false);

// Library data — only fetched the first time the palette is opened.
const { data: series, status, refresh } = useApi('/v2/Series', { key: FetchKeys.Series.All, server: false, lazy: true, immediate: false });
watch(open, (isOpen) => {
    if (isOpen && !series.value) refresh();
});

defineShortcuts({ meta_k: () => (open.value = !open.value), ctrl_k: () => (open.value = !open.value) });

const close = () => (open.value = false);
const go = (path: string) => {
    close();
    navigateTo(path);
};

const navItems: CommandPaletteItem[] = [
    { label: 'Library', icon: 'i-lucide-library', onSelect: () => go('/') },
    { label: 'Add series', icon: 'i-lucide-plus', onSelect: () => go('/search') },
    { label: 'Activity', icon: 'i-lucide-scroll-text', onSelect: () => go('/actions') },
    { label: 'Queue', icon: 'i-lucide-layers', onSelect: () => go('/queue') },
    { label: 'Settings', icon: 'i-lucide-settings', onSelect: () => go('/settings') },
];

const seriesItems = computed<CommandPaletteItem[]>(() =>
    (series.value ?? []).map((s) => ({
        label: s.name,
        icon: 'i-lucide-book-open',
        suffix: trackStateMeta(s).label,
        onSelect: () => go(`/series/${s.key}`),
    }))
);

const groups = computed<CommandPaletteGroup<CommandPaletteItem>[]>(() => [
    { id: 'nav', label: 'Go to', items: navItems },
    { id: 'library', label: 'Your library', items: seriesItems.value },
]);

const onSelect = (item?: CommandPaletteItem) => {
    item?.onSelect?.(new Event('select'));
};
</script>
