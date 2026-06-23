<template>
    <UModal v-bind="$props" :title="isEdit ? 'Edit Indexer' : 'Add Indexer'">
        <template #body>
            <UFormField label="Name">
                <UInput v-model="form.name" placeholder="Nyaa" class="w-full" :disabled="isEdit" />
            </UFormField>
            <UFormField label="Torznab/Newznab URL" class="mt-2">
                <UInput v-model="form.url" placeholder="http://indexer/api" class="w-full" />
            </UFormField>
            <UFormField label="API key" class="mt-2">
                <UInput
                    v-model="form.apiKey"
                    type="password"
                    class="w-full"
                    :placeholder="isEdit ? 'Leave blank to keep current key' : ''" />
            </UFormField>
            <UFormField label="Categories" class="mt-2" help="Comma-separated Torznab category IDs (e.g. 7030, 7000)">
                <UInput v-model="form.categories" placeholder="7030, 7000" class="w-full" />
            </UFormField>
            <UButton
                icon="i-lucide-save"
                :class="['mt-2 float-right', success === false ? 'animate-[shake_0.2s] bg-error' : '']"
                loading-auto
                :disabled="!allowSend"
                @click="submit"
                >Save</UButton
            >
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';

type ManualIndexerResponse = components['schemas']['ManualIndexerResponse'];

const props = defineProps<{ indexer?: ManualIndexerResponse | null }>();
const { $api } = useNuxtApp();

// Name keys the indexer (upsert), so it is locked on edit. The API key is never returned; on edit it
// starts blank and a blank submit keeps the stored secret.
const isEdit = computed(() => !!props.indexer);
const form = ref({
    name: props.indexer?.name ?? '',
    url: props.indexer?.url ?? '',
    apiKey: '',
    categories: (props.indexer?.categories ?? []).join(', '),
});

const allowSend = computed(() => !!form.value.name.trim() && !!form.value.url.trim());

const emit = defineEmits<{ close: [boolean] }>();
const { success, submit } = useConnectorModal({
    action: () =>
        $api('/v2/Settings/ManualIndexers', {
            method: 'POST',
            body: {
                name: form.value.name.trim(),
                url: form.value.url.trim(),
                apiKey: form.value.apiKey,
                categories: form.value.categories
                    .split(',')
                    .map((c) => Number.parseInt(c.trim(), 10))
                    .filter((n) => Number.isFinite(n)),
            },
        }),
    refreshKeys: FetchKeys.Settings.All,
    successTitle: isEdit.value ? 'Indexer updated' : 'Indexer added',
    onClose: () => emit('close', false),
});
</script>
