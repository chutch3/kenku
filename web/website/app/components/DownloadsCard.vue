<template>
    <UCard>
        <template #header>
            <SettingsHeader title="Downloads" subtitle="How chapter downloads behave." />
        </template>
        <UFormField label="Retry attempts" help="How many times a chapter download is tried before it parks in Needs attention.">
            <UInputNumber v-model="maxAttempts" :min="1" class="w-32" @blur="save" />
        </UFormField>
    </UCard>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const maxAttempts = ref(5);
const { data } = useApi('/v2/Settings/DownloadMaxAttempts', { key: 'Settings/DownloadMaxAttempts', server: false });
watch(data, (v) => {
    if (v != null) maxAttempts.value = v;
});

const save = async () => {
    await $api('/v2/Settings/DownloadMaxAttempts/{attempts}', { method: 'PATCH', path: { attempts: maxAttempts.value } });
    toast.add({ title: 'Retry attempts saved', icon: 'i-lucide-check', color: 'success', duration: 1500 });
};
</script>
