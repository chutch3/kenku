<template>
    <div class="flex flex-col gap-2">
        <div v-for="c in sources" :key="c.key" class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
            <FallbackImage :src="c.iconUrl" :alt="`${c.name} icon`" class="w-5 h-5 shrink-0" />
            <span class="text-sm grow truncate">{{ c.name }}</span>
            <UBadge color="neutral" variant="subtle" size="sm">{{ c.contentType === 'Comic' ? 'comic' : 'manga' }}</UBadge>
            <USwitch :model-value="c.enabled" :disabled="busy === c.name" @update:model-value="(v) => setEnabled(c.name, v)" />
        </div>
    </div>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();

const { data: connectors, refresh } = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, server: false });
// Global is the search-all pseudo-source; it has no enabled state of its own.
const sources = computed(() => (connectors.value ?? []).filter((c): c is typeof c & { name: string } => !!c.name && c.name !== 'Global'));

const busy = ref<string | null>(null);
const setEnabled = async (name: string, enabled: boolean) => {
    busy.value = name;
    try {
        await $api('/v2/SeriesSource/{MangaConnectorName}/SetEnabled/{Enabled}', {
            method: 'PATCH',
            path: { MangaConnectorName: name, Enabled: enabled },
        });
        await refresh();
    } finally {
        busy.value = null;
    }
};
</script>
