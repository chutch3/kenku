<template>
    <UModal v-bind="$props" title="Connect Prowlarr">
        <template #body>
            <UFormField label="URL">
                <UInput v-model="requestData.baseUrl" placeholder="http://prowlarr:9696" class="w-full" />
            </UFormField>
            <UFormField label="ApiKey">
                <template #description>
                    <ULink class="underline" :disabled="!apiLink" :to="apiLink" target="_blank" external no-prefetch
                        >Get your API Key (Settings → General)</ULink
                    >
                </template>
                <UInput v-model="requestData.apiKey" class="w-full" />
            </UFormField>
            <UButton
                icon="i-lucide-link"
                :class="['mt-2 float-right', success == false ? 'animate-[shake_0.2s] bg-error' : '']"
                loading-auto
                :disabled="!allowSend"
                @click="connect"
                >Connect</UButton
            >
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type SetProwlarrRecord = components['schemas']['SetProwlarrRecord'];
const { $api } = useNuxtApp();

const requestData = ref<SetProwlarrRecord>({ baseUrl: '', apiKey: '' });
const allowSend = computed(() => requestData.value.baseUrl && requestData.value.apiKey);

const apiLink = computed(() => {
    if (!requestData.value.baseUrl) return undefined;
    try {
        const url = new URL(requestData.value.baseUrl);
        return `${url.origin}/settings/general`;
    } catch {
        return undefined;
    }
});

const success = ref<boolean | undefined>(undefined);
const emit = defineEmits<{ close: [boolean] }>();
const connect = async () => {
    try {
        await $api('/v2/Settings/Prowlarr', { method: 'PATCH', body: requestData.value });
        await refreshNuxtData(FetchKeys.Settings.All);
        emit('close', false);
        success.value = true;
    } catch {
        success.value = false;
        setTimeout(() => (success.value = undefined), 200);
    }
};
</script>
