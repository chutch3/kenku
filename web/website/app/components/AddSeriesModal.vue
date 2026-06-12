<template>
    <UModal v-model:open="open" :title="series.name">
        <template #body>
            <AddSeriesForm :series="series" @added="onFormAdded" />
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type MinimalSeries = components['schemas']['MinimalSeries'];

defineProps<{ series: MinimalSeries }>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'added', payload: { libraryId: string; download: boolean }): void }>();

const onFormAdded = (payload: { libraryId: string; download: boolean }) => {
    emit('added', payload);
    open.value = false;
};
</script>
