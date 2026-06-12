<template>
    <div class="flex items-end gap-2">
        <UFormField label="Genre rails" description="AniList genres that each get their own rail on the Discover page.">
            <UInputTags v-model="genres" placeholder="Add a genre…" class="w-72" />
        </UFormField>
        <UButton variant="soft" loading-auto @click="save">Save</UButton>
    </div>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
const genres = ref<string[]>([]);
watch(settings, (s) => { if (s) genres.value = [...(s.discoveryGenres ?? [])]; }, { immediate: true });

const save = async () => {
    await $api('/v2/Settings/DiscoveryGenres', { method: 'PATCH', body: genres.value });
    await refreshNuxtData(FetchKeys.Settings.All);
    toast.add({ title: 'Discovery genres saved', icon: 'i-lucide-check', color: 'success' });
};
</script>
