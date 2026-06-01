<template>
    <UModal v-bind="$props" :title="isEdit ? 'Edit Download Client' : 'Add Download Client'">
        <template #body>
            <UFormField label="Name">
                <UInput v-model="requestData.name" placeholder="qBittorrent" class="w-full" />
            </UFormField>
            <UFormField label="Type" class="mt-2">
                <USelect v-model="requestData.type" :items="typeOptions" class="w-full" />
            </UFormField>
            <UFormField label="URL" class="mt-2">
                <UInput v-model="requestData.baseUrl" placeholder="http://qbittorrent:8080" class="w-full" />
            </UFormField>
            <UFormField label="Username" class="mt-2">
                <UInput v-model="requestData.username" class="w-full" />
            </UFormField>
            <UFormField label="Password" class="mt-2">
                <UInput v-model="requestData.password" type="password" class="w-full" />
            </UFormField>
            <UFormField label="Category" class="mt-2">
                <UInput v-model="requestData.category" placeholder="kenku" class="w-full" />
            </UFormField>
            <UFormField label="Priority" class="mt-2">
                <UInput v-model.number="requestData.priority" type="number" class="w-full" />
            </UFormField>
            <UFormField class="mt-2">
                <UCheckbox v-model="requestData.enabled" label="Enabled" />
            </UFormField>
            <UButton
                icon="i-lucide-save"
                :class="['mt-2 float-right', success === false ? 'animate-[shake_0.2s] bg-error' : '']"
                loading-auto
                :disabled="!allowSend"
                @click="save"
                >Save</UButton
            >
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';

type SetDownloadClientRecord = components['schemas']['SetDownloadClientRecord'];
type DownloadClientConfig = components['schemas']['DownloadClientConfig'];

const props = defineProps<{ client?: DownloadClientConfig | null }>();
const { $api } = useNuxtApp();

const isEdit = computed(() => !!props.client);
const typeOptions = ['QBittorrent'];

const requestData = ref<SetDownloadClientRecord>({
    id: props.client?.id ?? 0,
    name: props.client?.name ?? '',
    type: props.client?.type ?? 'QBittorrent',
    baseUrl: props.client?.baseUrl ?? '',
    username: props.client?.username ?? '',
    password: props.client?.password ?? '',
    category: props.client?.category ?? '',
    enabled: props.client?.enabled ?? true,
    priority: props.client?.priority ?? 1,
});

const allowSend = computed(() => !!requestData.value.name && !!requestData.value.baseUrl);

const success = ref<boolean | undefined>(undefined);
const emit = defineEmits<{ close: [boolean] }>();

const save = async () => {
    try {
        await $api('/v2/Settings/DownloadClients', {
            method: isEdit.value ? 'PUT' : 'POST',
            body: requestData.value,
        });
        await refreshNuxtData(FetchKeys.Settings.All);
        emit('close', false);
        success.value = true;
    } catch {
        success.value = false;
        setTimeout(() => (success.value = undefined), 200);
    }
};
</script>
