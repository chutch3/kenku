<template>
    <USkeleton v-if="libraryId === undefined" class="w-full h-8" />
    <USelect
        v-else
        v-model="library"
        :default-value="libraryId ?? undefined"
        placeholder="Choose a library…"
        icon="i-lucide-library-big"
        color="secondary"
        :items="libraries?.map((l) => l.key)"
        :class="[libraryId ? '' : 'ring-warning animate-[pulse_1s]']"
        :loading="loading"
        @change="onLibrarySelectChange">
        <template #default="{ modelValue }">
            <p v-if="modelValue">
                <span class="mr-2">{{ libraries?.find((l) => l.key == modelValue)?.libraryName }}</span>
                <span class="text-secondary">({{ libraries?.find((l) => l.key == modelValue)?.basePath }})</span>
            </p>
        </template>
        <template #item="{ item }">
            <p>
                <span class="mr-2">{{ libraries?.find((l) => l.key == item)?.libraryName }}</span>
                <span class="text-secondary">({{ libraries?.find((l) => l.key == item)?.basePath }})</span>
            </p>
        </template>
    </USelect>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();

export interface LibrarySelectProps {
    seriesId: string;
    libraryId?: string | null;
}

const props = defineProps<LibrarySelectProps>();

const library = ref();
const { data: libraries } = await useApi('/v2/FileLibrary', { server: false });

const loading = ref(false);
const onLibrarySelectChange = async () => {
    if (!library.value) return;
    loading.value = true;

    await $api('/v2/Series/{SeriesId}/ChangeLibrary/{LibraryId}', {
        method: 'POST',
        path: { SeriesId: props.seriesId, LibraryId: library.value },
    });
    await refreshNuxtData(FetchKeys.Series.Id(props.seriesId));
    loading.value = false;
    emit('libraryChanged', library.value);
};

const emit = defineEmits<{ (e: 'libraryChanged', id?: string): void }>();
</script>
