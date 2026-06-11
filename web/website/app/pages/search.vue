<template>
    <KenkuPage title="Add Series">
        <div class="reveal flex flex-col gap-5 pt-1 pb-24">
            <!-- Search bar -->
            <UCard class="w-full max-w-3xl mx-auto" :ui="{ body: 'sm:p-4' }">
                <div class="flex flex-col gap-3">
                    <div class="flex gap-2">
                        <UInput
                            v-model="query"
                            class="grow"
                            size="lg"
                            icon="i-lucide-search"
                            autofocus
                            placeholder="Search by title, or paste a series URL…"
                            :disabled="busy"
                            @keydown.enter="performSearch" />
                        <UButton
                            size="lg"
                            color="primary"
                            icon="i-lucide-search"
                            :disabled="busy || !query"
                            :loading="busy"
                            @click="performSearch">
                            Search
                        </UButton>
                    </div>
                    <div class="flex flex-wrap items-center gap-1.5">
                        <span class="text-xs text-muted mr-1">Source</span>
                        <UButton
                            v-for="c in connectors"
                            :key="c.key"
                            size="sm"
                            :color="selectedConnector?.key == c.key ? 'primary' : 'neutral'"
                            :variant="selectedConnector?.key == c.key ? 'soft' : 'outline'"
                            :disabled="busy"
                            @click="connectorClick(c)">
                            <template #leading>
                                <FallbackImage :src="c.iconUrl" :alt="`${c.name} icon`" class="h-lh" />
                            </template>
                            {{ c.name === 'Global' ? 'All sources' : c.name }}
                        </UButton>
                    </div>
                </div>
            </UCard>

            <!-- Loading -->
            <div v-if="busy" :class="gridClass">
                <USkeleton
                    v-for="n in 8"
                    :key="n"
                    class="max-sm:w-[var(--mangacover-width-sm)] max-sm:h-[var(--mangacover-height-sm)] w-(--mangacover-width) h-(--mangacover-height) rounded-lg" />
            </div>

            <!-- Results -->
            <template v-else-if="searchResult.length">
                <p class="text-sm text-muted">
                    {{ searchResult.length }} result<span v-if="searchResult.length > 1">s</span> for
                    <span class="text-highlighted font-medium">{{ searchQuery }}</span>
                    <span class="text-dimmed"> · click one to add it to your library</span>
                </p>
                <SeriesCardList :series="searchResult" @click="openResult" />
            </template>

            <!-- No results -->
            <div v-else-if="searched" class="flex flex-col items-center gap-2 py-16 text-center">
                <UIcon name="i-lucide-search-x" class="size-8 text-dimmed" />
                <p class="text-muted">
                    No results for <span class="text-highlighted">{{ searchQuery }}</span
                    >.
                </p>
                <p class="text-dimmed text-sm">Try a different title, or pick another source.</p>
            </div>

            <!-- Initial hint -->
            <div v-else class="flex flex-col items-center gap-3 py-16 text-center">
                <KenkuMark :size="52" class="opacity-80" />
                <p class="font-display text-lg text-highlighted">Find something to hoard</p>
                <p class="text-muted max-w-md">
                    Search across your connectors by title, or paste a series URL and Kenku will detect the source automatically.
                </p>
            </div>

            <AddSeriesModal v-if="pendingAdd" v-model:open="addModalOpen" :series="pendingAdd" @added="onAdded" />
        </div>
    </KenkuPage>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type MangaConnector = components['schemas']['SeriesSource'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const { $api } = useNuxtApp();

const gridClass =
    'grid min-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width),_1fr))] max-sm:grid-cols-[repeat(auto-fill,_minmax(var(--mangacover-width-sm),_1fr))] gap-4';

const { data: connectors } = await useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, server: false });

const query = ref<string>();
const connector = useState<MangaConnector | undefined>('search-connector', () => undefined);
const selectedConnector = computed(
    () => connector.value ?? connectors.value?.find((c) => c.name === 'Global') ?? connectors.value?.find((c) => c.enabled)
);
const busy = ref(false);
const searched = ref(false);

const isUrl = (input: string): boolean => {
    try {
        new URL(input);
        return true;
    } catch {
        return false;
    }
};

const connectorClick = (c: MangaConnector) => {
    connector.value = c;
    if (query.value) performSearch();
};

const searchResult = useState<MinimalSeries[]>(() => []);
const searchQuery = useState<string>(() => '');

const performSearch = async () => {
    if (!query.value || busy.value) return;
    busy.value = true;
    searchQuery.value = query.value;
    try {
        searchResult.value = await search(query.value);
    } catch {
        searchResult.value = [];
    } finally {
        searched.value = true;
        busy.value = false;
        refreshNuxtData(FetchKeys.Series.All);
    }
};

const search = async (q: string): Promise<MinimalSeries[]> => {
    if (isUrl(q)) {
        const data = await $api('/v2/Search', { query: { url: JSON.stringify(q) } });
        if (!data) return [];
        connector.value = connectors.value?.find((c) => c.name == data.sourceIds[0]?.mangaConnectorName);
        return [data];
    }
    if (!selectedConnector.value?.name) return [];
    const data = await $api('/v2/Search/{MangaConnectorName}/{Query}', {
        path: { MangaConnectorName: selectedConnector.value.name, Query: q },
        method: 'GET',
    });
    return data ?? [];
};

// Deep links (e.g. Discover cards) land here with ?q= and optionally ?source=; run the search
// on arrival so adding is one click away.
onMounted(() => {
    const q = useRoute().query.q;
    const source = useRoute().query.source;
    if (typeof q !== 'string' || !q) return;
    query.value = q;
    if (typeof source === 'string')
        connector.value = connectors.value?.find((c) => c.name === source) ?? connector.value;
    performSearch();
});

const pendingAdd = ref<MinimalSeries | null>(null);
const addModalOpen = ref(false);
const toast = useToast();

const openResult = (m: MinimalSeries) => {
    if (m.fileLibraryId) {
        navigateTo(`/series/${m.key}?return=${useRoute().fullPath}`);
        return;
    }
    pendingAdd.value = m;
    addModalOpen.value = true;
};

const onAdded = ({ libraryId, download }: { libraryId: string; download: boolean }) => {
    const added = pendingAdd.value;
    if (!added) return;
    // Flip the card in place so the result shows its real tracked state without leaving the page.
    added.fileLibraryId = libraryId;
    if (added.sourceIds[0]) added.sourceIds[0].useForDownload = download;
    refreshNuxtData([FetchKeys.Series.All, FetchKeys.Series.Rollup]);
    toast.add({
        title: download ? `Added ${added.name} — downloading` : `Added ${added.name}`,
        icon: download ? 'i-lucide-cloud-download' : 'i-lucide-bookmark-plus',
        color: 'success',
        actions: [{ label: 'Open series', onClick: () => void navigateTo(`/series/${added.key}`) }],
    });
};

useHead({ title: 'Search Series' });
</script>
