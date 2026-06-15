<template>
    <SeriesDetailPage :series="series" :rollup="rollup" :kind="kind">
        <div class="grid gap-3 max-xl:grid-flow-row-dense min-2xl:grid-cols-[70%_auto] min-xl:grid-cols-[60%_auto] relative min-xl:h-full">
            <ChaptersList :manga-id="mangaId" :kind="kind" class="min-xl:h-full min-xl:overflow-y-scroll" />
            <div class="flex flex-col gap-3">
                <!-- Download sources lead: turning a source on is the primary control on this page. -->
                <UCard v-if="series">
                    <template #header>
                        <div>
                            <h2 class="font-display text-lg font-semibold text-highlighted">Download sources</h2>
                            <p class="text-xs text-muted">Sites Kenku pulls chapters from — toggle which to use.</p>
                        </div>
                    </template>
                    <div class="flex flex-col gap-2">
                        <div
                            v-for="src in sortedSources"
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
                                <UButton size="xs" variant="ghost" color="neutral" icon="i-lucide-link-2" aria-label="Re-match source" @click="rematchSource = src" />
                            </UTooltip>
                            <USwitch
                                :model-value="src.useForDownload"
                                :disabled="!series?.fileLibraryId"
                                :aria-label="`Download from ${src.mangaConnectorName}`"
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
                    <!-- Comics are effectively always flat (issues carry no volume number), so the
                         layout choice only exists for manga. -->
                    <LibraryLayoutSelect
                        v-if="series?.fileLibraryId && kind !== 'comic'"
                        :manga-id="mangaId"
                        class="w-full mt-2"
                        @layout-changed="refreshNuxtData(FetchKeys.Series.Id(mangaId))" />
                    <LooseChapters v-if="series?.fileLibraryId" :manga-id="mangaId" :kind="kind" class="w-full mt-2" />
                </UCard>

                <!-- Advanced metadata is collapsed so the primary controls above aren't crowded. -->
                <UCard>
                    <button
                        type="button"
                        class="flex w-full items-center gap-2 text-left"
                        :aria-expanded="advancedOpen"
                        @click="advancedOpen = !advancedOpen">
                        <UIcon name="i-lucide-sliders-horizontal" class="size-4 text-muted" />
                        <span class="font-display text-lg font-semibold text-highlighted grow">Advanced</span>
                        <UIcon :name="advancedOpen ? 'i-lucide-chevron-up' : 'i-lucide-chevron-down'" class="text-dimmed" />
                    </button>
                    <div v-show="advancedOpen" class="flex flex-col gap-3 mt-3">
                        <!-- Volume mapping is a manga concept; comics enrich via Metron instead. -->
                        <MetadataSourceLink v-if="kind === 'manga'" :manga-id="mangaId" :series-name="series?.name" />
                        <SeriesMetadataFetcherTable :manga-id="mangaId" :kind="kind" />
                    </div>
                </UCard>
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
const route = useRoute();
const mangaId = route.params.mangaId as string;
const toast = useToast();

const { series, rollup, kind, refreshingData, refreshData, refreshRollups, setRequestedFrom, syncNow } = useSeriesDetail(mangaId);

const sortedSources = computed(() =>
    [...(series.value?.sourceIds ?? [])].sort((a, b) => a.mangaConnectorName.localeCompare(b.mangaConnectorName)));

const advancedOpen = ref(false);

const deleteOpen = ref(false);
const onDeleted = async () => {
    toast.add({ title: `Deleted ${series.value?.name ?? 'series'}`, icon: 'i-lucide-trash', color: 'neutral' });
    await refreshNuxtData(FetchKeys.Series.All);
    navigateTo('/');
};

const rematchSource = ref<components['schemas']['SeriesSourceId'] | null>(null);
const onRematched = async () => {
    rematchSource.value = null;
    toast.add({ title: 'Source re-matched', description: 'A fresh chapter sync is queued.', icon: 'i-lucide-link-2', color: 'success' });
    await refreshData();
    await refreshRollups();
};

defineShortcuts({ meta_r: { usingInput: true, handler: () => refreshData() } });

useHead({ title: 'Series' });
</script>
