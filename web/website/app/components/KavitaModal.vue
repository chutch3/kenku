<template>
    <UModal v-bind="$props" title="Connect Kavita">
        <template #body>
            <UFormField label="URL">
                <UInput v-model="requestData.url" placeholder="https://" class="w-full" />
            </UFormField>
            <UFormField label="ApiKey">
                <UInput v-model="requestData.apiKey" class="w-full" />
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
type CreateKavitaRecord = components['schemas']['CreateKavitaRecord'];
const { $api } = useNuxtApp();

const requestData = ref<CreateKavitaRecord>({ url: '', apiKey: '' });

const allowSend = computed(() => requestData.value.url && requestData.value.apiKey);

const emit = defineEmits<{ close: [boolean] }>();
const { success, submit } = useConnectorModal({
    action: () => $api('/v2/LibraryConnector/Kavita', { method: 'PUT', body: requestData.value }),
    refreshKeys: FetchKeys.Libraries.All,
    successTitle: 'Kavita connected',
    onClose: () => emit('close', false),
});
</script>
