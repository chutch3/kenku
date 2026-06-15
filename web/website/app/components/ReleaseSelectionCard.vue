<template>
    <UCard>
        <template #header>
            <SettingsHeader title="Release selection" subtitle="How a torrent release is picked for a comic chapter." />
        </template>
        <div class="flex flex-col gap-3 max-w-md">
            <UFormField label="Minimum seeders" help="Releases with fewer seeders are never picked.">
                <UInputNumber v-model="selection.minSeeders" :min="0" class="w-32" />
            </UFormField>
            <UFormField label="Preferred tokens" help="Filename tokens that rank a release higher.">
                <UInputTags v-model="selection.preferredTokens" placeholder="cbz" />
            </UFormField>
            <UFormField label="Blocked tokens" help="Filename tokens that exclude a release outright.">
                <UInputTags v-model="selection.blockedTokens" placeholder="cbr, pdf" />
            </UFormField>
        </div>
        <template #footer>
            <UButton icon="i-lucide-save" color="primary" class="w-fit" loading-auto @click="save">Save release selection</UButton>
        </template>
    </UCard>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const selection = reactive({ minSeeders: 2, preferredTokens: ['cbz'] as string[], blockedTokens: ['cbr', 'pdf'] as string[] });
const { data } = useApi('/v2/Settings/ReleaseSelection', { key: 'Settings/ReleaseSelection', server: false });
watch(data, (v) => {
    if (!v) return;
    selection.minSeeders = v.minSeeders ?? 2;
    selection.preferredTokens = [...(v.preferredTokens ?? [])];
    selection.blockedTokens = [...(v.blockedTokens ?? [])];
});

const save = async () => {
    await $api('/v2/Settings/ReleaseSelection', {
        method: 'PATCH',
        body: { minSeeders: selection.minSeeders, preferredTokens: selection.preferredTokens, blockedTokens: selection.blockedTokens },
    });
    toast.add({ title: 'Release selection saved', icon: 'i-lucide-check', color: 'success' });
};
</script>
