<template>
    <UFormField label="Genre rails" description="AniList genres that each get their own rail on the Discover page — changes apply immediately.">
        <!-- add-on-blur: a half-typed genre commits when focus leaves the input instead of being
             silently dropped; every committed change saves itself (no Save button to race). -->
        <UInputTags v-model="genres" add-on-blur placeholder="Add a genre…" class="w-72" />
    </UFormField>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
const genres = ref<string[]>([]);
watch(settings, (s) => { if (s) genres.value = [...(s.discoveryGenres ?? [])]; }, { immediate: true });

// Self-stabilizing: edits that already match the saved list (initial load, post-save refresh) are
// no-ops, so this can't loop with the settings watch above.
watch(genres, async (next) => {
    const saved = settings.value?.discoveryGenres ?? [];
    if (next.length === saved.length && next.every((g, i) => g === saved[i])) return;
    try {
        await $api('/v2/Settings/DiscoveryGenres', { method: 'PATCH', body: next });
        await refreshNuxtData(FetchKeys.Settings.All);
        toast.add({ title: 'Discovery genres saved', icon: 'i-lucide-check', color: 'success', duration: 1500 });
    } catch {
        // A chip the server never accepted must not sit there looking saved.
        genres.value = [...saved];
        toast.add({ title: "Couldn't save discovery genres", icon: 'i-lucide-triangle-alert', color: 'error' });
    }
});
</script>
