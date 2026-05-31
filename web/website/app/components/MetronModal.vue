<template>
    <UModal v-bind="$props" title="Connect Metron">
        <template #body>
            <UFormField label="Username">
                <template #description>
                    <ULink class="underline" to="https://metron.cloud/accounts/login/" target="_blank" external no-prefetch
                        >Your metron.cloud account</ULink
                    >
                </template>
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
type SetMetronRecord = components['schemas']['SetMetronRecord'];
const { $api } = useNuxtApp();

const requestData = ref<SetMetronRecord>({ username: '', password: '' });
const allowSend = computed(() => requestData.value.username && requestData.value.password);

const success = ref<boolean | undefined>(undefined);
const emit = defineEmits<{ close: [boolean] }>();
const connect = async () => {
    try {
        await $api('/v2/Settings/Metron', { method: 'PATCH', body: requestData.value });
        await refreshNuxtData(FetchKeys.Settings.All);
        emit('close', false);
        success.value = true;
    } catch {
        success.value = false;
        setTimeout(() => (success.value = undefined), 200);
    }
};
</script>
