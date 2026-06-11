<template>
    <div>
        <USelect
            v-model="layout"
            :items="layoutOptions"
            placeholder="Layout"
            icon="i-lucide-folder-tree"
            color="secondary"
            :loading="loading"
            class="w-full"
            @change="onLayoutChange" />
        <p v-if="kind === 'comic'" class="text-xs text-muted mt-1">
            Comics download as finished archives, so Flat usually fits; volume layouts only group archives that carry a volume number (e.g. TPBs).
        </p>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';

type LibraryLayout = components['schemas']['LibraryLayout'];

const { $api } = useNuxtApp();

export interface LibraryLayoutSelectProps {
    mangaId: string;
    kind?: SeriesKind;
}

const props = defineProps<LibraryLayoutSelectProps>();
const emit = defineEmits<{ (e: 'layoutChanged', layout: LibraryLayout): void }>();

const layoutOptions = [
    { label: 'Flat — all chapters in one folder', value: 'Flat' },
    { label: 'Volume folders — chapters grouped in Vol N/', value: 'VolumeFolder' },
    { label: 'Volume CBZ — one .cbz per volume', value: 'VolumeCBZ' },
];

const layout = ref<LibraryLayout>('Flat');

// Seed the current layout from the volumes endpoint (the Series payload doesn't carry it).
const { data: volumes } = await useApi('/v2/Series/{MangaId}/volumes', { path: { MangaId: props.mangaId }, server: false });
watchEffect(() => {
    if (volumes.value?.layout) layout.value = volumes.value.layout as LibraryLayout;
});

const loading = ref(false);
const onLayoutChange = async () => {
    loading.value = true;
    await $api('/v2/Series/{MangaId}/libraryLayout', { method: 'PUT', path: { MangaId: props.mangaId }, body: { layout: layout.value } });
    await refreshNuxtData(FetchKeys.Series.Id(props.mangaId));
    loading.value = false;
    emit('layoutChanged', layout.value);
};
</script>
