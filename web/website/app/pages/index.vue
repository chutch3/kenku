<template>
    <ClientOnly>
        <LoadingPage :loading="status === 'pending'">
            <div
                v-if="!series || series.length === 0"
                class="reveal flex flex-col items-center justify-center gap-5 h-[calc(100dvh-var(--ui-header-height)-2rem)] text-center px-6">
                <KenkuMark :size="76" class="opacity-90" />
                <div class="flex flex-col gap-1">
                    <p class="font-display text-2xl text-highlighted">The hoard is empty</p>
                    <p class="text-muted max-w-sm">The kenku has nothing to mimic yet. Track your first series to begin the collection.</p>
                </div>
                <UButton to="/search" color="primary" icon="i-lucide-plus" size="lg">Add a series</UButton>
            </div>
            <div v-else class="min-md:mx-4 max-md:mx-0 mt-2">
                <!-- Library toolbar: filter / status / sort -->
                <div class="reveal flex flex-wrap items-center gap-2 mb-1">
                    <UInput
                        v-model="filterText"
                        icon="i-lucide-search"
                        placeholder="Filter your library…"
                        class="grow min-w-50"
                        :ui="{ trailing: 'pe-1' }">
                        <template v-if="filterText" #trailing>
                            <UButton color="neutral" variant="link" size="sm" icon="i-lucide-x" @click="filterText = ''" />
                        </template>
                    </UInput>
                    <USelect v-model="statusFilter" :items="statusOptions" icon="i-lucide-filter" class="w-44" />
                    <USelect v-model="sortBy" :items="sortOptions" icon="i-lucide-arrow-down-up" class="w-40" />
                    <p class="font-mono text-xs text-dimmed text-nowrap ml-auto">
                        {{ filtered.length }}<span class="text-muted/60"> / {{ series.length }}</span> series
                    </p>
                </div>
                <SeriesCardList v-if="filtered.length" :series="filtered" @click="(m) => navigateTo(`/series/${m.key}`)" />
                <div v-else class="flex flex-col items-center gap-2 py-20 text-center">
                    <UIcon name="i-lucide-search-x" class="size-8 text-dimmed" />
                    <p class="text-muted">No series match your filters.</p>
                    <UButton variant="link" color="primary" @click="resetFilters">Clear filters</UButton>
                </div>
            </div>
        </LoadingPage>
        <template #fallback>
            <LoadingPage :loading="true" />
        </template>
    </ClientOnly>
</template>

<script setup lang="ts">
const { data: series, refresh, status } = await useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
onMounted(() => refresh());

const filterText = ref('');
const statusFilter = ref<'all' | TrackState>('all');
const sortBy = ref<'name-asc' | 'name-desc'>('name-asc');

const statusOptions = [
    { label: 'All series', value: 'all' },
    { label: 'Downloading', value: 'downloading' },
    { label: 'Paused', value: 'paused' },
    { label: 'Not tracked', value: 'untracked' },
];
const sortOptions = [
    { label: 'Name A → Z', value: 'name-asc' },
    { label: 'Name Z → A', value: 'name-desc' },
];

const filtered = computed(() => {
    let list = series.value ?? [];
    const q = filterText.value.trim().toLowerCase();
    if (q) list = list.filter((s) => s.name.toLowerCase().includes(q));
    if (statusFilter.value !== 'all') list = list.filter((s) => seriesTrackState(s) === statusFilter.value);
    list = [...list].sort((a, b) => a.name.localeCompare(b.name));
    if (sortBy.value === 'name-desc') list.reverse();
    return list;
});

const resetFilters = () => {
    filterText.value = '';
    statusFilter.value = 'all';
};

useHead({ title: 'Kenku' });
</script>
