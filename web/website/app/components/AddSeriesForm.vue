<template>
    <div class="flex flex-col gap-4">
        <p class="text-sm text-muted">{{ description }}</p>
        <div class="flex gap-4">
            <FallbackImage :src="series.coverUrl" :alt="series.name" class="w-24 rounded-md shrink-0 self-start" />
            <MDC v-if="series.description" :value="series.description" class="text-sm text-muted line-clamp-6 min-w-0" />
        </div>

        <USkeleton v-if="chaptersPending" class="h-6 w-56" />
        <UAlert
            v-else-if="chaptersError"
            color="error"
            variant="subtle"
            icon="i-lucide-triangle-alert"
            :title="`${sourceName} could not deliver a chapter list`"
            :description="chaptersError" />
        <UAlert
            v-else-if="chapters.length === 0"
            color="warning"
            variant="subtle"
            icon="i-lucide-circle-alert"
            title="This source reports no chapters"
            description="Adding it now would download nothing — consider a different source." />
        <p v-else class="text-sm flex items-center gap-1.5">
            <UIcon name="i-lucide-book-open" class="text-secondary" />
            {{ chapters.length }} chapter<span v-if="chapters.length > 1">s</span> available from {{ sourceName }}
        </p>

        <template v-if="canAdd">
            <div v-if="libraries && libraries.length">
                <span class="text-xs text-muted">Save to</span>
                <USelect v-model="libraryId" :items="libraryItems" class="w-full mt-1" />
            </div>
            <div v-else-if="libraries" class="flex flex-col gap-2">
                <p class="text-sm text-muted">A library is the folder where Kenku saves downloaded files — set one up first.</p>
                <UButton :to="`/settings?return=${$route.fullPath}`" icon="i-lucide-folder-plus" color="primary" class="w-fit">
                    Set up a library
                </UButton>
            </div>
        </template>

        <div class="flex gap-2 w-full justify-end">
            <template v-if="canAdd">
                <UButton
                    color="neutral"
                    variant="outline"
                    icon="i-lucide-bookmark-plus"
                    :disabled="!libraryId"
                    :loading="adding === 'only'"
                    @click="add(false)">
                    Add only
                </UButton>
                <UButton
                    color="primary"
                    icon="i-lucide-cloud-download"
                    :disabled="!libraryId"
                    :loading="adding === 'download'"
                    @click="add(true)">
                    Add &amp; download
                </UButton>
            </template>
            <!-- Nothing to download here, so adding is blocked — point the user at the full search to
                 find a source that actually has it. -->
            <UButton
                v-else-if="!chaptersPending"
                color="primary"
                icon="i-lucide-search"
                @click="searchOtherSources">
                Search other sources
            </UButton>
        </div>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type MinimalSeries = components['schemas']['MinimalSeries'];
type ChapterPreview = components['schemas']['ChapterPreview'];

const props = defineProps<{ series: MinimalSeries }>();
const emit = defineEmits<{ (e: 'added', payload: { libraryId: string; download: boolean }): void }>();

const { $api } = useNuxtApp();

const source = computed(() => props.series.sourceIds[0]);
const sourceName = computed(() => source.value?.mangaConnectorName ?? 'source');

const { data: connectors } = await useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, server: false });
const kind = computed(() => seriesKind(props.series, connectors.value));
const description = computed(() => {
    if (kind.value !== 'comic') return `From ${sourceName.value}`;
    const torrentBacked = connectors.value?.find((c) => c.name === sourceName.value)?.kind === 'Torrent';
    return torrentBacked ? `From ${sourceName.value} — comic, delivered via your indexers` : `From ${sourceName.value} — comic`;
});

const { data: libraries } = await useApi('/v2/FileLibrary', { key: FetchKeys.FileLibraries, server: false });
const libraryItems = computed(() => (libraries.value ?? []).map((l) => ({ label: `${l.libraryName} (${l.basePath})`, value: l.key })));
const libraryId = ref<string>();
watch(libraries, (libs) => (libraryId.value ??= libs?.[0]?.key), { immediate: true });

// Live preview from the connector: '0 chapters' or a broken source must be visible while a
// different source can still be picked — not after the series sits silently in the library.
const chapters = ref<ChapterPreview[]>([]);
const chaptersPending = ref(false);
const chaptersError = ref<string | null>(null);
watch(
    source,
    async (src) => {
        if (!src) return;
        chaptersPending.value = true;
        chaptersError.value = null;
        try {
            chapters.value =
                (await $api('/v2/Search/{MangaConnectorName}/Chapters', {
                    path: { MangaConnectorName: src.mangaConnectorName },
                    query: { ConnectorSeriesId: src.idOnConnectorSite },
                })) ?? [];
        } catch (e) {
            chaptersError.value = e instanceof Error ? ((e as { data?: string }).data ?? e.message) : String(e);
        } finally {
            chaptersPending.value = false;
        }
    },
    { immediate: true }
);

// Only allow adding once a source has resolved at least one chapter — adding a 0-chapter (or broken)
// source would download nothing, so we send the user to search a different source instead.
const canAdd = computed(() => !chaptersPending.value && !chaptersError.value && chapters.value.length > 0);
const searchOtherSources = () => {
    void navigateTo(`/search?q=${encodeURIComponent(props.series.name)}`);
};

const adding = ref<'download' | 'only' | null>(null);
const add = async (download: boolean) => {
    if (!libraryId.value || !source.value) return;
    adding.value = download ? 'download' : 'only';
    try {
        await $api('/v2/Series/{MangaId}/ChangeLibrary/{LibraryId}', {
            method: 'POST',
            path: { MangaId: props.series.key, LibraryId: libraryId.value },
            query: {
                connectorName: source.value.mangaConnectorName,
                connectorSeriesId: source.value.idOnConnectorSite,
                download,
            },
        });
        emit('added', { libraryId: libraryId.value, download });
    } finally {
        adding.value = null;
    }
};
</script>
