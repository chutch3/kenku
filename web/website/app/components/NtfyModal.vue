<template>
    <UModal v-bind="$props" title="Connect Ntfy">
        <template #body>
            <UFormField label="Name">
                <UInput v-model="requestData.name" placeholder="Name" class="w-full" />
            </UFormField>
            <UFormField label="URL">
                <UInput v-model="requestData.url" placeholder="https://" class="w-full" />
            </UFormField>
            <UFormField label="Username">
                <UInput v-model="requestData.username" class="w-full" />
            </UFormField>
            <UFormField label="Password">
                <UInput v-model="requestData.password" class="w-full" />
            </UFormField>
            <UFormField label="Priority">
                <UInput v-model="requestData.priority" type="number" class="w-full" />
            </UFormField>
            <UFormField label="Topic">
                <UInput v-model="requestData.topic" class="w-full" />
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
type CreateNtfyConnectorRecord = components['schemas']['CreateNtfyConnectorRecord'];
const { $api } = useNuxtApp();

const requestData = ref<CreateNtfyConnectorRecord>({ name: 'Ntfy', url: '', priority: 3, username: '', password: '', topic: 'Kenku' });

const allowSend = computed(
    () =>
        requestData.value.name &&
        requestData.value.url &&
        requestData.value.username &&
        requestData.value.password &&
        requestData.value.topic &&
        requestData.value.priority >= 1 &&
        requestData.value.priority <= 5
);

const emit = defineEmits<{ close: [boolean] }>();
const { success, submit } = useConnectorModal({
    action: () => $api('/v2/NotificationConnector/Ntfy', { method: 'PUT', body: requestData.value }),
    refreshKeys: FetchKeys.NotificationConnectors.All,
    successTitle: 'Ntfy connected',
    onClose: () => emit('close', false),
});
</script>
