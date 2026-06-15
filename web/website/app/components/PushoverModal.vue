<template>
    <UModal v-bind="$props" title="Connect Pushover">
        <template #body>
            <UFormField label="Name">
                <UInput v-model="requestData.name" placeholder="Name" class="w-full" />
            </UFormField>
            <UFormField label="AppToken">
                <UInput v-model="requestData.appToken" class="w-full" />
            </UFormField>
            <UFormField label="Username">
                <UInput v-model="requestData.username" class="w-full" />
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
type CreatePushoverConnectorRecord = components['schemas']['CreatePushoverConnectorRecord'];
const { $api } = useNuxtApp();

const requestData = ref<CreatePushoverConnectorRecord>({ name: 'Pushover', appToken: '', username: '' });

const allowSend = computed(() => requestData.value.name && requestData.value.appToken && requestData.value.username);

const emit = defineEmits<{ close: [boolean] }>();
const { success, submit } = useConnectorModal({
    action: () => $api('/v2/NotificationConnector/Pushover', { method: 'PUT', body: requestData.value }),
    refreshKeys: FetchKeys.NotificationConnectors.All,
    successTitle: 'Pushover connected',
    onClose: () => emit('close', false),
});
</script>
