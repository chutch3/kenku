<template>
    <UModal v-bind="$props" title="Connect Gotify">
        <template #body>
            <UFormField label="Name">
                <UInput v-model="requestData.name" placeholder="Name" class="w-full" />
            </UFormField>
            <UFormField label="URL">
                <UInput v-model="requestData.url" placeholder="https://" class="w-full" />
            </UFormField>
            <UFormField label="AppToken">
                <UInput v-model="requestData.appToken" class="w-full" />
            </UFormField>
            <UFormField label="Priority">
                <UInput v-model="requestData.priority" type="number" class="w-full" />
            </UFormField>
            <UButton
                icon="i-lucide-link"
                :class="['mt-2 float-right', success === false ? 'animate-[shake_0.2s] bg-error' : '']"
                loading-auto
                :disabled="!allowSend"
                @click="submit"
                >Connect</UButton
            >
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type CreateGotifyConnectorRecord = components['schemas']['CreateGotifyConnectorRecord'];
const { $api } = useNuxtApp();

const requestData = ref<CreateGotifyConnectorRecord>({ name: 'Gotify', url: '', appToken: '', priority: 3 });

const allowSend = computed(
    () =>
        requestData.value.name &&
        requestData.value.url &&
        requestData.value.appToken &&
        requestData.value.priority >= 1 &&
        requestData.value.priority <= 5
);

const emit = defineEmits<{ close: [boolean] }>();
const { success, submit } = useConnectorModal({
    action: () => $api('/v2/NotificationConnector/Gotify', { method: 'PUT', body: requestData.value }),
    refreshKeys: FetchKeys.NotificationConnectors.All,
    successTitle: 'Gotify connected',
    onClose: () => emit('close', false),
});
</script>
