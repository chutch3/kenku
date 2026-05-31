<template>
    <UModal v-bind="$props" title="Connect Torrent Client">
        <template #body>
            <UFormField label="URL">
                <UInput v-model="requestData.baseUrl" placeholder="http://qbittorrent:8080" class="w-full" />
            </UFormField>
            <UFormField label="Username">
                <UInput v-model="requestData.username" class="w-full" />
            </UFormField>
            <UFormField label="Password">
                <UInput v-model="requestData.password" type="password" class="w-full" />
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
type SetTorrentClientRecord = components['schemas']['SetTorrentClientRecord'];
const { $api } = useNuxtApp();

const requestData = ref<SetTorrentClientRecord>({ baseUrl: '', username: '', password: '' });
const allowSend = computed(() => requestData.value.baseUrl);

const success = ref<boolean | undefined>(undefined);
const emit = defineEmits<{ close: [boolean] }>();
const connect = async () => {
    try {
        await $api('/v2/Settings/TorrentClient', { method: 'PATCH', body: requestData.value });
        await refreshNuxtData(FetchKeys.Settings.All);
        emit('close', false);
        success.value = true;
    } catch {
        success.value = false;
        setTimeout(() => (success.value = undefined), 200);
    }
};
</script>
