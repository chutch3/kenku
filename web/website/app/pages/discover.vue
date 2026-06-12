<template>
    <KenkuPage title="Discover">
        <div class="reveal flex flex-col gap-8 pt-1 pb-24">
            <p class="text-sm text-muted -mb-4">
                What's moving right now — click anything new to add it; anything you already hoard opens in place.
            </p>

            <div v-if="loading" class="flex gap-3">
                <USkeleton v-for="n in 8" :key="n" class="h-40 w-28 rounded-lg shrink-0" />
            </div>

            <DiscoveryRail
                title="Trending manga"
                subtitle="AniList · right now"
                :entries="manga"
                :library="library"
                :resolving="resolving"
                @pick="(e) => pick(e)"
                @open="openSeries" />
            <DiscoveryRail
                title="Fresh comics"
                subtitle="GetComics · latest posts"
                :entries="comics"
                :library="library"
                :resolving="resolving"
                @pick="(e) => pick(e, 'GetComics')"
                @open="openSeries" />
            <DiscoveryRail
                title="New & popular"
                subtitle="AniList · started this year"
                :entries="newManga"
                :library="library"
                :resolving="resolving"
                @pick="(e) => pick(e)"
                @open="openSeries" />
            <DiscoveryRail
                title="Top rated"
                subtitle="AniList · all-time"
                :entries="topRated"
                :library="library"
                :resolving="resolving"
                @pick="(e) => pick(e)"
                @open="openSeries" />
            <DiscoveryGenreRail
                v-for="genre in genres"
                :key="genre"
                :genre="genre"
                :library="library"
                :resolving="resolving"
                @pick="(e) => pick(e)"
                @open="openSeries" />
            <DiscoveryRail title="From the feeds" subtitle="hot on reddit" :entries="feed" external />

            <div v-if="!loading && !manga?.length && !comics?.length && !feed?.length" class="flex flex-col items-center gap-3 py-16 text-center">
                <KenkuMark :size="52" class="opacity-80" />
                <p class="font-display text-lg text-highlighted">Nothing to show right now</p>
                <p class="text-muted max-w-md">The discovery sources didn't answer — they're retried hourly, so check back soon.</p>
            </div>
            <AddSeriesModal v-if="pendingAdd" v-model:open="addModalOpen" :series="pendingAdd" @added="onAdded" />
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
const { data: feed } = useApi('/v2/Discover/Feed', { key: FetchKeys.Discover.Feed, lazy: true, server: false });
const { data: library } = useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, lazy: true, server: false });
const genres = computed(() => settings.value?.discoveryGenres ?? []);

const loading = computed(() => (mangaPending.value || comicsPending.value) && !manga.value?.length && !comics.value?.length);

const goSearch = (title: string, source?: string) =>
    navigateTo(`/search?q=${encodeURIComponent(title)}${source ? `&source=${source}` : ''}`);
const openSeries = (key: string) => navigateTo(`/series/${key}`);

const { searchByUrl, searchByConnector } = useSeriesSearch();
const { pendingAdd, addModalOpen, startAdd, onAdded } = useAddSeriesFlow();
const resolving = ref<string | null>(null);

// Click-to-add: resolve the card to a real connector series and pop the add modal in place.
// Source rails (GetComics) resolve exactly via their post URL; AniList entries only have a title,
// so anything short of a confident Global name match falls back to the prefilled search page.
const pick = async (entry: Entry, source?: string) => {
    if (resolving.value) return;
    resolving.value = entry.url || entry.title || '';
    try {
        const match =
            source && entry.url
                ? await searchByUrl(entry.url)
                : (await searchByConnector('Global', entry.title ?? '')).find(
                      (s) => normalizeTitle(s.name) === normalizeTitle(entry.title)
                  );
        if (match) startAdd(match);
        else await goSearch(entry.title ?? '', source);
    } catch {
        await goSearch(entry.title ?? '', source);
    } finally {
        resolving.value = null;
    }
};

useHead({ title: 'Discover' });
</script>
