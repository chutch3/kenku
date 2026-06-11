<template>
    <KenkuPage title="Discover">
        <div class="reveal flex flex-col gap-8 pt-1 pb-24">
            <p class="text-sm text-muted -mb-4">
                What's moving right now — click anything new to search and add it; anything you already hoard opens in place.
            </p>

            <div v-if="loading" class="flex gap-3">
                <USkeleton v-for="n in 8" :key="n" class="h-40 w-28 rounded-lg shrink-0" />
            </div>

            <DiscoveryRail
                title="Trending manga"
                subtitle="AniList · right now"
                :entries="manga"
                :library="library"
                @pick="(e) => goSearch(e.title ?? '')"
                @open="openSeries" />
            <DiscoveryRail
                title="Fresh comics"
                subtitle="GetComics · latest posts"
                :entries="comics"
                :library="library"
                @pick="(e) => goSearch(e.title ?? '', 'GetComics')"
                @open="openSeries" />
            <DiscoveryRail title="From the feeds" subtitle="hot on reddit" :entries="feed" external />

            <div v-if="!loading && !manga?.length && !comics?.length && !feed?.length" class="flex flex-col items-center gap-3 py-16 text-center">
                <KenkuMark :size="52" class="opacity-80" />
                <p class="font-display text-lg text-highlighted">Nothing to show right now</p>
                <p class="text-muted max-w-md">The discovery sources didn't answer — they're retried hourly, so check back soon.</p>
            </div>
        </div>
    </KenkuPage>
</template>

<script setup lang="ts">
const { data: manga, pending: mangaPending } = useApi('/v2/Discover/Manga', { key: FetchKeys.Discover.Manga, lazy: true, server: false });
const { data: comics, pending: comicsPending } = useApi('/v2/Discover/Comics', { key: FetchKeys.Discover.Comics, lazy: true, server: false });
const { data: feed } = useApi('/v2/Discover/Feed', { key: FetchKeys.Discover.Feed, lazy: true, server: false });
const { data: library } = useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });

const loading = computed(() => (mangaPending.value || comicsPending.value) && !manga.value?.length && !comics.value?.length);

const goSearch = (title: string, source?: string) =>
    navigateTo(`/search?q=${encodeURIComponent(title)}${source ? `&source=${source}` : ''}`);
const openSeries = (key: string) => navigateTo(`/series/${key}`);

useHead({ title: 'Discover' });
</script>
