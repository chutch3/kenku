<template>
    <SeriesDetailPage :series="series" :rollup="rollup" :kind="kind">
        <div class="grid gap-3 max-xl:grid-flow-row-dense min-2xl:grid-cols-[70%_auto] min-xl:grid-cols-[60%_auto] relative min-xl:h-full">
            <ChaptersList :manga-id="mangaId" class="min-xl:h-full min-xl:overflow-y-scroll" />
            <div class="flex flex-col gap-3">
                <!-- Storage: where files land. -->
                <UCard>
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
                        :kind="kind"
                        class="w-full mt-2"
                        @layout-changed="refreshNuxtData(FetchKeys.Series.Id(mangaId))" />
                    <LooseChapters v-if="series?.fileLibraryId" :manga-id="mangaId" :kind="kind" class="w-full mt-2" />
                </UCard>

                <!-- Download sources: which sites to pull from. -->
                <UCard v-if="series">
                    <template #header>
                        <div>
                            <h2 class="font-display text-lg font-semibold text-highlighted">Download sources</h2>
                            <p class="text-xs text-muted">Sites Kenku pulls chapters from — toggle which to use.</p>
                        </div>
                    </template>
                    <div class="flex flex-col gap-2">
                        <div
                            v-for="src in [...series.sourceIds].sort((a, b) => (a.mangaConnectorName < b.mangaConnectorName ? -1 : 1))"
                            :key="src.key"
                            class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                            <SourceIcon v-bind="src" />
                            <span class="text-sm grow truncate">{{ src.mangaConnectorName }}</span>
                            <span
                                class="font-mono text-[0.65rem] uppercase tracking-wide"
                                :class="src.useForDownload ? 'text-success' : 'text-dimmed'">
                                {{ src.useForDownload ? 'On' : 'Off' }}
                            </span>
                            <UTooltip text="Re-match this link (fix a wrong entry)">
                                <UButton size="xs" variant="ghost" color="neutral" icon="i-lucide-link-2" @click="rematchSource = src" />
                            </UTooltip>
                            <USwitch
                                :model-value="src.useForDownload"
                                :disabled="!series?.fileLibraryId"
                                @update:model-value="(v) => setRequestedFrom(src.mangaConnectorName, v)" />
                        </div>
                    </div>
                    <RematchSourceModal
                        v-if="rematchSource"
                        :open="!!rematchSource"
                        :manga-id="mangaId"
                        :source="rematchSource"
                        :series-name="series?.name"
                        @update:open="(v) => { if (!v) rematchSource = null; }"
                        @rematched="onRematched" />
                </UCard>

                <!-- Metadata: volume mapping is a manga concept; comics enrich via Metron instead. -->
                <MetadataSourceLink v-if="kind === 'manga'" :manga-id="mangaId" :series-name="series?.name" />
                <SeriesMetadataFetcherTable :manga-id="mangaId" :kind="kind" />
            </div>
        </div>
        <template #actions>
            <template v-if="series">
                <UButton
                    icon="i-lucide-brick-wall-shield"
                    :to="`/actions?mangaId=${mangaId}&return=${$route.fullPath}`"
                    variant="soft"
                    color="secondary" />
                <UButton trailing-icon="i-lucide-merge" :to="`/series/${series?.key}/merge?return=${$route.fullPath}`" color="secondary"
                    >Merge</UButton
                >
                <UTooltip text="Sync chapters & cover now">
                    <UButton variant="soft" color="secondary" icon="i-lucide-cloud-download" loading-auto @click="syncNow" />
                </UTooltip>
                <UButton variant="soft" color="warning" icon="i-lucide-trash" @click="deleteOpen = true" />
                <UTooltip text="Reload" :kbds="['meta', 'R']">
                    <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" :loading="refreshingData" @click="refreshData()" />
                </UTooltip>
                <DeleteSeriesModal v-model:open="deleteOpen" :manga-id="mangaId" :series-name="series?.name" @deleted="onDeleted" />
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

const series = ref<components['schemas']['Series'] | null>(null);

const { data: rollups, refresh: refreshRollups } = await useApi('/v2/Series/Rollup', {
    key: FetchKeys.Series.Rollup,
    lazy: true,
    server: false,
});
onMounted(() => refreshRollups());
const rollup = computed(() => (rollups.value ?? []).find((r) => r.mangaId === mangaId) ?? null);

const { data: connectors } = await useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, lazy: true, server: false });
const kind = computed(() => (series.value ? seriesKind(series.value, connectors.value) : 'manga'));

if (import.meta.client) {
    const { data } = await useApi('/v2/Series/{MangaId}', {
        path: { MangaId: mangaId },
        key: FetchKeys.Series.Id(mangaId),
        onResponseError: (e) => {
            console.error(e);
            navigateTo('/');
        },
        lazy: true,
        server: false,
    });
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

const deleteOpen = ref(false);
const onDeleted = async () => {
    toast.add({ title: `Deleted ${series.value?.name ?? 'series'}`, icon: 'i-lucide-trash', color: 'neutral' });
    await refreshNuxtData(FetchKeys.Series.All);
    navigateTo('/');
};

const toast = useToast();
const syncNow = async () => {
    await $api('/v2/Series/{MangaId}/Sync', { method: 'POST', path: { MangaId: mangaId } });
    toast.add({ title: 'Sync queued', description: 'Chapters and cover refresh from your sources.', icon: 'i-lucide-cloud-download', color: 'success' });
    await refreshRollups();
};

const rematchSource = ref<components['schemas']['SeriesSourceId'] | null>(null);
const onRematched = async () => {
    rematchSource.value = null;
    toast.add({ title: 'Source re-matched', description: 'A fresh chapter sync is queued.', icon: 'i-lucide-link-2', color: 'success' });
    await refreshData();
    await refreshRollups();
};

const refreshingData = ref(false);
const refreshData = async (quiet = false) => {
    refreshingData.value = true;
    await refreshNuxtData([
        FetchKeys.Series.Id(mangaId),
        FetchKeys.Series.Rollup,
        FetchKeys.Metadata.Series(mangaId),
        FetchKeys.FileLibraries,
        FetchKeys.Chapters.Series(mangaId),
    ]);
    refreshingData.value = false;
    if (!quiet) toast.add({ title: 'Series refreshed', icon: 'i-lucide-check', color: 'neutral', duration: 1500 });
};

// While jobs for this series are in flight (e.g. after "Sync now"), poll the rollup and refresh the
// page when they drain — updates land without leaving and coming back.
const activeJobs = computed(() => (rollup.value ? rollup.value.queuedJobs + rollup.value.runningJobs : 0));
useSeriesActivity(activeJobs, { poll: () => refreshRollups(), onDrained: () => refreshData(true) });

defineShortcuts({ meta_r: { usingInput: true, handler: () => refreshData() } });

useHead({ title: 'Series' });
</script>
