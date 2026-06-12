<template>
    <KenkuPage title="Discover">
        <div class="reveal flex flex-col gap-8 pt-1 pb-24">
            <p class="text-sm text-muted -mb-4">
                What's moving right now — click anything new to add it; anything you already hoard opens in place.
            </p>

            <div v-if="loading" class="flex gap-3">
                <USkeleton
                    v-for="n in 6"
                    :key="n"
                    class="max-sm:h-[var(--mangacover-height-sm)] h-(--mangacover-height) max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) rounded-lg shrink-0" />
            </div>

            <section v-if="mangaRails.some((r) => r.entries?.length) || genres.length" class="flex flex-col gap-8">
                <div class="flex items-center gap-3 -mb-2">
                    <h2 class="font-display text-2xl font-bold text-highlighted">Manga</h2>
                    <span class="h-px w-16 bg-vermillion-500 shadow-[0_0_8px_var(--color-vermillion-500)]" />
                </div>
                <DiscoveryRail
                    v-for="rail in mangaRails"
                    :key="rail.title"
                    :title="rail.title"
                    :subtitle="rail.subtitle"
                    :entries="rail.entries"
                    :library="library"
                    @pick="(e) => pick(e)"
                    @open="openSeries" />
                <DiscoveryGenreRail
                    v-for="genre in genres"
                    :key="genre"
                    :genre="genre"
                    :library="library"
                    @pick="(e) => pick(e)"
                    @open="openSeries" />
            </section>

            <section v-if="comics?.length" class="flex flex-col gap-8">
                <div class="flex items-center gap-3 -mb-2">
                    <h2 class="font-display text-2xl font-bold text-highlighted">Comics</h2>
                    <span class="h-px w-16 bg-vermillion-500 shadow-[0_0_8px_var(--color-vermillion-500)]" />
                </div>
                <DiscoveryRail
                    title="Fresh releases"
                    subtitle="GetComics · latest posts"
                    :entries="comics"
                    :library="library"
                    @pick="(e) => pick(e, 'GetComics')"
                    @open="openSeries" />
            </section>

            <DiscoveryRail title="From the feeds" subtitle="hot on reddit" :entries="feed" external />
            <div v-if="feedStarved" class="flex items-center gap-2 text-xs text-muted">
                <UIcon name="i-lucide-rss" />
                <span>
                    Nothing from the feeds yet — they're fetched hourly and reddit may be rate-limiting.
                    Settings → Maintenance can trigger a fetch now.
                </span>
            </div>

            <div v-if="empty" class="flex flex-col items-center gap-3 py-16 text-center">
                <KenkuMark :size="52" class="opacity-80" />
                <p class="font-display text-lg text-highlighted">Nothing to show right now</p>
                <p class="text-muted max-w-md">The discovery sources didn't answer — they're retried hourly, so check back soon.</p>
            </div>
            <DiscoverAddModal
                v-if="activeEntry"
                v-model:open="addOpen"
                :entry="activeEntry"
                :source="activeSource"
                @added="notifyAdded" />
        </div>
    </KenkuPage>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Entry = components['schemas']['DiscoveryEntry'];

const { data: manga, pending: mangaPending } = useApi('/v2/Discover/Manga', { key: FetchKeys.Discover.Manga, lazy: true, server: false });
const { data: comics, pending: comicsPending } = useApi('/v2/Discover/Comics', { key: FetchKeys.Discover.Comics, lazy: true, server: false });
const { data: newManga } = useApi('/v2/Discover/Manga/New', { key: FetchKeys.Discover.New, lazy: true, server: false });
const { data: topRated } = useApi('/v2/Discover/Manga/TopRated', { key: FetchKeys.Discover.TopRated, lazy: true, server: false });
const { data: feed, pending: feedPending } = useApi('/v2/Discover/Feed', { key: FetchKeys.Discover.Feed, lazy: true, server: false });
const { data: library } = useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, lazy: true, server: false });
const genres = computed(() => settings.value?.discoveryGenres ?? []);

const mangaRails = computed(() => [
    { title: 'Trending', subtitle: 'AniList · right now', entries: manga.value },
    { title: 'New & popular', subtitle: 'AniList · started this year', entries: newManga.value },
    { title: 'Top rated', subtitle: 'AniList · all-time', entries: topRated.value },
]);

const loading = computed(() => (mangaPending.value || comicsPending.value) && !manga.value?.length && !comics.value?.length);
// Genre rails fetch inside their own components, so the page can't see their entries — any
// configured genre suppresses the empty state rather than contradicting a populated rail.
const empty = computed(
    () =>
        !loading.value &&
        mangaRails.value.every((r) => !r.entries?.length) &&
        !comics.value?.length &&
        !feed.value?.length &&
        !genres.value.length
);
// An empty feed rail hides itself; with feeds configured that silence is undiagnosable — say why.
const feedStarved = computed(
    () => !feedPending.value && !!settings.value?.discoveryFeeds?.length && !feed.value?.length
);

const openSeries = (key: string) => navigateTo(`/series/${key}`);

// Click-to-add: the modal opens instantly with the entry's own details and resolves the real
// connector series inside itself (DiscoverAddModal) — the click never waits on a search.
const { notifyAdded } = useAddSeriesFlow();
const activeEntry = ref<Entry | null>(null);
const activeSource = ref<string | undefined>();
const addOpen = ref(false);

const pick = (entry: Entry, source?: string) => {
    activeEntry.value = entry;
    activeSource.value = source;
    addOpen.value = true;
};

useHead({ title: 'Discover' });
</script>
