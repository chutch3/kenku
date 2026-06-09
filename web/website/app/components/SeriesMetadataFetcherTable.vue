<template>
    <UCard :ui="{ body: 'p-0 sm:p-0' }">
        <template #header>
            <div>
                <h2 class="font-display text-lg font-semibold text-highlighted">Series details</h2>
                <p class="text-xs text-muted">
                    Pull title, description &amp; cover from {{ kind === 'comic' ? 'Metron' : 'MyAnimeList' }}.
                </p>
            </div>
        </template>
        <UTable
            v-if="metadataFetchers && metadata"
            :data="visibleFetchers"
            :columns="[
                { header: 'Name', id: 'name' },
                { header: '', id: 'link' },
            ]">
            <template #name-cell="{ row }">
                <UTooltip :text="metadata.find((me) => me.metadataFetcherName == row.original)?.identifier ?? undefined">
                    <p class="text-toned">{{ row.original }}</p></UTooltip
                >
            </template>
            <template #link-cell="{ row }">
                <div class="flex flex-row gap-2 justify-end">
                    <UButton
                        v-if="metadata.find((me) => me.metadataFetcherName === row.original)"
                        icon="i-lucide-unlink"
                        loading-auto
                        @click="unlinkMetadataFetcher(row.original)" />
                    <UTooltip v-if="metadata.find((me) => me.metadataFetcherName === row.original)" text="Update Metadata">
                        <UButton icon="i-lucide-refresh-ccw-dot" loading-auto @click="updateMetadata(row.original)" />
                    </UTooltip>
                    <UButton
                        v-if="metadata.find((me) => me.metadataFetcherName === row.original) === undefined"
                        :to="`/series/${mangaId}/linkMetadata/${row.original}?return=${$route.fullPath}`"
                        loading-auto
                        icon="i-lucide-link" />
                </div>
            </template>
        </UTable>
    </UCard>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string; kind?: SeriesKind }>();
const mangaId = props.mangaId;

const { $api } = useNuxtApp();

const { data: metadataFetchers } = await useApi('/v2/MetadataFetcher', { key: FetchKeys.Metadata.Fetchers, lazy: true, server: false });
const { data: metadata } = await useApi('/v2/MetadataFetcher/Links/{MangaId}', {
    path: { MangaId: mangaId },
    key: FetchKeys.Metadata.Series(mangaId),
    lazy: true,
    server: false,
});

// Metron is the comic enrichment source, MyAnimeList the manga one — offering the wrong one is the
// "metadata feels duplicated" confusion. Anything already linked stays visible regardless.
const visibleFetchers = computed(() =>
    (metadataFetchers.value ?? []).filter((f) => {
        if (metadata.value?.some((me) => me.metadataFetcherName === f)) return true;
        if (!props.kind) return true;
        return props.kind === 'comic' ? f === 'Metron' : f !== 'Metron';
    })
);

const unlinkMetadataFetcher = async (metadataFetcherName: string) => {
    await $api('/v2/MetadataFetcher/{MetadataFetcherName}/Unlink/{MangaId}', {
        method: 'POST',
        path: { MangaId: mangaId, MetadataFetcherName: metadataFetcherName },
    });
    await refreshNuxtData(FetchKeys.Metadata.Series(mangaId));
};

const updateMetadata = async (metadataFetcherName: string) => {
    await $api('/v2/MetadataFetcher/{MetadataFetcherName}/Update/{MangaId}', {
        method: 'POST',
        path: { MangaId: mangaId, MetadataFetcherName: metadataFetcherName },
    });
    await refreshNuxtData(FetchKeys.Series.Id(mangaId));
};
</script>
