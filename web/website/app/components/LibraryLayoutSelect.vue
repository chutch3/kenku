<template>
    <USelect
        v-model="layout"
        :items="LAYOUT_OPTIONS"
        placeholder="Layout"
        icon="i-lucide-folder-tree"
        color="secondary"
        :loading="loading"
        @change="onLayoutChange" />
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();

export interface LibraryLayoutSelectProps {
    seriesId: string;
}

const props = defineProps<LibraryLayoutSelectProps>();
const emit = defineEmits<{ (e: 'layoutChanged', layout: LibraryLayout): void }>();

const layout = ref<LibraryLayout>('Flat');

// Seed the current layout from the volumes endpoint (the Series payload doesn't carry it).
const { data: volumes } = await useApi('/v2/Series/{SeriesId}/volumes', { path: { SeriesId: props.seriesId }, server: false });
watchEffect(() => {
    if (volumes.value?.layout) layout.value = volumes.value.layout as LibraryLayout;
});

const loading = ref(false);
const onLayoutChange = async () => {
    loading.value = true;
    await $api('/v2/Series/{SeriesId}/libraryLayout', { method: 'PUT', path: { SeriesId: props.seriesId }, body: { layout: layout.value } });
    await refreshNuxtData(FetchKeys.Series.Id(props.seriesId));
    loading.value = false;
    emit('layoutChanged', layout.value);
};
</script>
