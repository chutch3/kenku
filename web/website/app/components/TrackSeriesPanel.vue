<template>
    <UCard :ui="{ root: 'ring-2 ring-primary/30' }">
        <template #header>
            <div class="flex items-center gap-2">
                <UIcon name="i-lucide-bookmark-plus" class="text-primary size-5" />
                <h2 class="font-display text-lg font-semibold text-highlighted">Track this series</h2>
            </div>
        </template>

        <p class="text-sm text-muted mb-3">
            You're previewing this series. Add it to your library to start tracking it and downloading new chapters.
        </p>

        <!-- Exactly one library: one click to add. -->
        <template v-if="libraries && libraries.length === 1">
            <UButton
                icon="i-lucide-bookmark-plus"
                color="primary"
                class="w-full justify-center"
                :loading="tracking"
                @click="track(libraries[0]!.key)">
                Add to {{ libraries[0]!.libraryName }}
            </UButton>
            <p class="text-xs text-dimmed mt-2">Saves to {{ libraries[0]!.basePath }} and begins downloading from your enabled sources.</p>
        </template>

        <!-- Several libraries: choose where to save. -->
        <template v-else-if="libraries && libraries.length > 1">
            <span class="text-xs text-muted">Save to</span>
            <LibrarySelect :manga-id="mangaId" :library-id="null" class="w-full mt-1" @library-changed="$emit('tracked')" />
            <p class="text-xs text-dimmed mt-2">Choosing a library adds the series and begins downloading from your enabled sources.</p>
        </template>

        <!-- No libraries yet. -->
        <div v-else-if="libraries" class="flex flex-col gap-3">
            <p class="text-sm text-muted">You don't have any libraries yet — a library is the folder where Kenku saves downloaded files.</p>
            <UButton :to="`/settings?return=${$route.fullPath}`" icon="i-lucide-folder-plus" color="primary" class="w-fit">
                Set up a library
            </UButton>
        </div>

        <USkeleton v-else class="h-9 w-full" />
    </UCard>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string }>();
const emit = defineEmits<{ (e: 'tracked'): void }>();

const { $api } = useNuxtApp();
const route = useRoute();
const { data: libraries } = await useApi('/v2/FileLibrary', { server: false });

const tracking = ref(false);
const track = async (libraryId: string) => {
    tracking.value = true;
    try {
        await $api('/v2/Series/{MangaId}/ChangeLibrary/{LibraryId}', {
            method: 'POST',
            path: { MangaId: props.mangaId, LibraryId: libraryId },
            query: {
                connectorName: route.query.connectorName as string | undefined,
                connectorSeriesId: route.query.connectorSeriesId as string | undefined,
            },
        });
        emit('tracked');
    } finally {
        tracking.value = false;
    }
};
</script>
