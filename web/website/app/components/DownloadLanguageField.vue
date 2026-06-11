<template>
    <div class="flex items-end gap-2">
        <UFormField label="Download language" description="Language requested from sources that offer chapters in more than one.">
            <UInput v-model="language" placeholder="en" class="w-40" />
        </UFormField>
        <UButton variant="soft" loading-auto :disabled="!language" @click="save">Save</UButton>
    </div>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: current } = useApi('/v2/Settings/DownloadLanguage', { key: 'Settings/DownloadLanguage', server: false });
const language = ref('');
watch(current, (v) => { if (v) language.value = v; }, { immediate: true });

const save = async () => {
    await $api('/v2/Settings/DownloadLanguage/{Language}', { method: 'PATCH', path: { Language: language.value } });
    toast.add({ title: `Downloading in '${language.value}'`, icon: 'i-lucide-check', color: 'success' });
};
</script>
