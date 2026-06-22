<template>
    <UFormField label="Community feeds" description="Subreddits whose hot posts feed the Discover “From the community” rail — comma-separated.">
        <div class="flex items-end gap-2">
            <UInput v-model="feedsText" placeholder="manga, comicbooks" class="w-72" />
            <UButton variant="soft" loading-auto :disabled="!dirty" @click="save">Save</UButton>
        </div>
    </UFormField>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });

const parse = (text: string) => text.split(',').map((f) => f.trim()).filter((f) => f.length > 0);

const feedsText = ref('');
watch(settings, (s) => { feedsText.value = (s?.discoveryFeeds ?? []).join(', '); }, { immediate: true });

const dirty = computed(() => parse(feedsText.value).join(',') !== (settings.value?.discoveryFeeds ?? []).join(','));

const save = async () => {
    const feeds = parse(feedsText.value);
    try {
        await $api('/v2/Settings/DiscoveryFeeds', { method: 'PATCH', body: feeds });
        await refreshNuxtData(FetchKeys.Settings.All);
        toast.add({ title: 'Community feeds saved', icon: 'i-lucide-check', color: 'success', duration: 1500 });
    } catch {
        toast.add({ title: "Couldn't save community feeds", icon: 'i-lucide-triangle-alert', color: 'error' });
    }
};
</script>
