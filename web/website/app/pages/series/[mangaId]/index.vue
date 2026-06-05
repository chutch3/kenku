<template>
    <SeriesDetailPage :series="series">
        <div class="grid gap-3 max-xl:grid-flow-row-dense min-2xl:grid-cols-[70%_auto] min-xl:grid-cols-[60%_auto] relative min-xl:h-full">
            <ChaptersList
                v-if="!isSearchResult || (series && series.fileLibraryId)"
                :manga-id="mangaId"
                class="min-xl:h-full min-xl:overflow-y-scroll" />
            <div class="flex flex-col gap-3">
                <!-- Untracked preview: make "add it" the obvious primary action. -->
                <TrackSeriesPanel
                    v-if="isSearchResult && !series?.fileLibraryId"
                    :manga-id="mangaId"
                    @tracked="refreshNuxtData(FetchKeys.Series.Id(mangaId))" />

                <!-- Storage: where files land. -->
                <UCard v-if="!isSearchResult || series?.fileLibraryId" :class="[flashDownloading ? 'animate-[flash_0.75s_ease_0.5s]' : '']">
                    <template #header>
                        <div>
                            <h2 class="font-display text-lg font-semibold text-highlighted">Storage</h2>
                            <p class="text-xs text-muted">Where Kenku saves downloaded files.</p>
                        </div>
                    </template>
                    <LibrarySelect
                        :manga-id="mangaId"
                        :library-id="series?.fileLibraryId"
                        class="w-full"
                        @library-changed="refreshNuxtData(FetchKeys.Series.Id(mangaId))" />
                    <LibraryLayoutSelect
                        v-if="series?.fileLibraryId"
                        :manga-id="mangaId"
                        class="w-full mt-2"
                        @layout-changed="refreshNuxtData(FetchKeys.Series.Id(mangaId))" />
                    <LooseChapters v-if="series?.fileLibraryId" :manga-id="mangaId" class="w-full mt-2" />
                </UCard>

                <!-- Download sources: which sites to pull from. -->
                <UCard v-if="series && (!isSearchResult || series.fileLibraryId)">
                    <template #header>
                        <div>
                            <h2 class="font-display text-lg font-semibold text-highlighted">Download sources</h2>
                            <p class="text-xs text-muted">Sites Kenku pulls chapters from — toggle which to use.</p>
                        </div>
                    </template>
                    <div class="flex flex-row gap-2 w-full flex-wrap">
                        <div
                            v-for="mangaconnectorId in [...series.sourceIds].sort((a, b) =>
                                a.mangaConnectorName < b.mangaConnectorName ? -1 : 1
                            )"
                            :key="mangaconnectorId.key"
                            class="bg-elevated p-1 rounded-lg w-fit flex items-center justify-center gap-2">
                            <SourceIcon v-bind="mangaconnectorId" />
                            <UTooltip
                                :text="
                                    mangaconnectorId.useForDownload ? 'Stop downloading from this website' : 'Download from this website'
                                ">
                                <UButton
                                    :icon="mangaconnectorId.useForDownload ? 'i-lucide-cloud-off' : 'i-lucide-cloud-download'"
                                    variant="ghost"
                                    :disabled="!series?.fileLibraryId"
                                    @click="setRequestedFrom(mangaconnectorId.mangaConnectorName, !mangaconnectorId.useForDownload)" />
                            </UTooltip>
                        </div>
                    </div>
                </UCard>

                <!-- Metadata: link to the canonical MangaDex entry. -->
                <MetadataSourceLink
                    v-if="!isSearchResult || (series && series.fileLibraryId)"
                    :manga-id="mangaId"
                    :series-name="series?.name" />
                <SeriesMetadataFetcherTable v-if="!isSearchResult || (series && series.fileLibraryId)" :manga-id="mangaId" />
            </div>
        </div>
        <template #actions>
            <template v-if="!isSearchResult || (series && series.fileLibraryId)">
                <UButton
                    icon="i-lucide-brick-wall-shield"
                    :to="`/actions?mangaId=${mangaId}&return=${$route.fullPath}`"
                    variant="soft"
                    color="secondary" />
                <UButton trailing-icon="i-lucide-merge" :to="`/series/${series?.key}/merge?return=${$route.fullPath}`" color="secondary"
                    >Merge</UButton
                >
                <UButton variant="soft" color="warning" icon="i-lucide-trash" @click="remove" />
                <UTooltip text="Reload" :kbds="['meta', 'R']">
                    <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" :loading="refreshingData" @click="refreshData" />
                </UTooltip>
            </template>
        </template>
    </SeriesDetailPage>
</template>

<script setup lang="ts">
import SeriesDetailPage from '~/components/SeriesDetailPage.vue';
import type { components } from '#open-fetch-schemas/api';
const { $api } = useNuxtApp();
const route = useRoute();
const mangaId = route.params.mangaId as string;
const connectorName = route.query.connectorName as string | undefined;
const connectorSeriesId = route.query.connectorSeriesId as string | undefined;

const flashDownloading = route.hash.substring(1) == 'download';

const isSearchResult = !!(connectorName && connectorSeriesId);

const series = ref<components['schemas']['Series'] | null>(null);

if (import.meta.client) {
    const fetcher = isSearchResult
        ? useApi('/v2/Search/{MangaConnectorName}/Series', {
              path: { MangaConnectorName: connectorName! },
              query: { ConnectorSeriesId: connectorSeriesId! },
              key: FetchKeys.Series.Id(mangaId),
              onResponseError: (e) => {
                  console.error(e);
                  navigateTo('/');
              },
              lazy: true,
              server: false,
          })
        : useApi('/v2/Series/{MangaId}', {
              path: { MangaId: mangaId },
              key: FetchKeys.Series.Id(mangaId),
              onResponseError: (e) => {
                  console.error(e);
                  navigateTo('/');
              },
              lazy: true,
              server: false,
          });
    const { data } = await fetcher;
    watch(
        data,
        (v) => {
            series.value = v ?? null;
        },
        { immediate: true }
    );
}

const setRequestedFrom = async (MangaConnectorName: string, IsRequested: boolean) => {
    await $api('/v2/Series/{MangaId}/DownloadFrom/{MangaConnectorName}/{IsRequested}', {
        method: 'PATCH',
        path: { MangaId: mangaId, MangaConnectorName: MangaConnectorName, IsRequested: IsRequested },
    });
    await refreshNuxtData(FetchKeys.Series.Id(mangaId));
};

const remove = async () => {
    await $api('/v2/Series/{MangaId}', { method: 'DELETE', path: { MangaId: mangaId } });
    await refreshNuxtData(FetchKeys.Series.All);
    navigateTo('/');
};

const refreshingData = ref(false);
const refreshData = async () => {
    refreshingData.value = true;
    await refreshNuxtData([
        FetchKeys.Series.Id(mangaId),
        FetchKeys.Metadata.Series(mangaId),
        FetchKeys.FileLibraries,
        FetchKeys.Chapters.All,
    ]);
    refreshingData.value = false;
};

defineShortcuts({ meta_r: { usingInput: true, handler: refreshData } });

useHead({ title: 'Series' });
</script>
