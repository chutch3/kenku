<template>
    <UFormField label="Genre rails" description="AniList genres that each get their own rail on the Discover page — changes apply immediately.">
        <USelectMenu
            v-model="genres"
            :items="available ?? []"
            multiple
            searchable
            placeholder="Pick genres…"
            class="w-72" />
    </UFormField>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
const { data: available } = useApi('/v2/Discover/Genres', { key: FetchKeys.Discover.Genres, server: false });

const same = (a: string[], b: string[]) => a.length === b.length && a.every((x, i) => x === b[i]);

const genres = ref<string[]>([]);
// Sync from settings only when it actually differs, so a post-save refetch doesn't reset the field.
watch(settings, (s) => {
    const saved = s?.discoveryGenres ?? [];
    if (!same(genres.value, saved)) genres.value = [...saved];
}, { immediate: true });

// Auto-save on selection change; the saved/echo case is a no-op, so this never loops with the sync above.
watch(genres, async (next) => {
    const saved = settings.value?.discoveryGenres ?? [];
    if (same(next, saved)) return;
    try {
        await $api('/v2/Settings/DiscoveryGenres', { method: 'PATCH', body: next });
        await refreshNuxtData(FetchKeys.Settings.All);
        toast.add({ title: 'Discovery genres saved', icon: 'i-lucide-check', color: 'success', duration: 1500 });
    } catch {
        genres.value = [...saved];
        toast.add({ title: "Couldn't save discovery genres", icon: 'i-lucide-triangle-alert', color: 'error' });
    }
});
</script>
