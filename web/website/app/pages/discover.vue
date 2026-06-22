<template>
    <KenkuPage title="Discover">
        <div class="reveal flex flex-col gap-10 pt-1 pb-24">
            <p class="text-sm text-muted -mb-6">
                Fresh picks across your sources — click to add, or open something you already have.
            </p>

            <div v-if="loading" class="flex gap-3">
                <USkeleton
                    v-for="n in 6"
                    :key="n"
                    class="max-sm:h-[var(--mangacover-height-sm)] h-(--mangacover-height) max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) rounded-lg shrink-0" />
            </div>

            <section v-if="mangaRails.some((r) => r.entries.length) || genresHaveContent" class="flex flex-col gap-6">
                <SectionLabel>Manga</SectionLabel>
                <DiscoveryRail
                    v-for="rail in mangaRails"
                    :key="rail.id"
                    :title="rail.label"
                    :entries="rail.entries"
                    :library="library"
                    @pick="(e) => pick(e, sourceFor(e))"
                    @open="openSeries" />
                <DiscoveryGenreRail
                    v-for="genre in genres"
                    :key="genre"
                    :genre="genre"
                    :library="library"
                    :exclude="mangaSeen"
                    @pick="(e) => pick(e)"
                    @open="openSeries"
                    @content="onGenreContent" />
            </section>

            <section v-if="comicRails.some((r) => r.entries.length)" class="flex flex-col gap-6">
                <SectionLabel>Comics</SectionLabel>
                <DiscoveryRail
                    v-for="rail in comicRails"
                    :key="rail.id"
                    :title="rail.label"
                    :entries="rail.entries"
                    :library="library"
                    @pick="(e) => pick(e, sourceFor(e))"
                    @open="openSeries" />
            </section>

            <section v-if="feed?.length || feedStarved" class="flex flex-col gap-6">
                <SectionLabel>From the community</SectionLabel>
                <DiscoveryRail title="Hot threads" subtitle="reddit · opens in a new tab" :entries="feed" external />
                <p v-if="feedStarved" class="flex items-center gap-2 text-xs text-muted">
                    <UIcon name="i-lucide-rss" class="shrink-0" />
                    Nothing from the feeds yet — fetched hourly, and reddit may be rate-limiting. Settings → Maintenance can trigger one now.
                </p>
            </section>

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

// One data-driven call drives every flat rail (Trending/Popular/Latest/New/Top-rated/comics). Genre
// rails and the reddit feed stay their own endpoints — they carry bespoke logic.
const { data: rails, pending: railsPending } = useApi('/v2/Discover/Rails', { key: FetchKeys.Discover.Rails, lazy: true, server: false });
const { data: feed, pending: feedPending } = useApi('/v2/Discover/Feed', { key: FetchKeys.Discover.Feed, lazy: true, server: false });
const { data: library } = useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, lazy: true, server: false });
const { data: connectors } = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, lazy: true, server: false });
const genres = computed(() => settings.value?.discoveryGenres ?? []);

// De-dupe across the manga rails in order: a title that trended and is also "top rated" only shows in
// the first rail. (Rails arrive already ordered from the endpoint.)
const mangaRails = computed(() => {
    const seen = new Set<string>();
    return (rails.value ?? [])
        .filter((r) => r.contentType === 'Manga')
        .map((r) => {
            const entries = (r.entries ?? []).filter((e) => !seen.has(normalizeTitle(e.title)));
            entries.forEach((e) => seen.add(normalizeTitle(e.title)));
            return { id: r.id ?? '', label: r.label ?? '', entries };
        });
});
const comicRails = computed(() =>
    (rails.value ?? [])
        .filter((r) => r.contentType === 'Comic')
        .map((r) => ({ id: r.id ?? '', label: r.label ?? '', entries: r.entries ?? [] }))
);

// Titles the manga rails already show — genre rails exclude them so they don't echo the same picks.
const mangaSeen = computed(() => mangaRails.value.flatMap((r) => r.entries.map((e) => normalizeTitle(e.title))));

// An entry resolves by-URL only when its source is a real connector (GetComics, MangaDex); AniList
// entries have no connector, so they resolve by title (no source) — exactly the old per-rail behaviour.
const sourceFor = (e: Entry): string | undefined =>
    connectors.value?.some((c) => c.name === e.source) ? (e.source ?? undefined) : undefined;

// Genre rails self-fetch and report whether they show anything; an unreported genre is treated as
// content (still loading) so the section/empty state never flash before it resolves.
const genreContent = reactive<Record<string, boolean>>({});
const onGenreContent = (genre: string, hasContent: boolean) => { genreContent[genre] = hasContent; };
const genresHaveContent = computed(() => genres.value.some((g) => genreContent[g] !== false));

const loading = computed(() => railsPending.value && !rails.value?.length);
const empty = computed(
    () =>
        !loading.value &&
        mangaRails.value.every((r) => !r.entries.length) &&
        !genresHaveContent.value &&
        comicRails.value.every((r) => !r.entries.length) &&
        !feed.value?.length
);
// An empty feed rail hides itself; with feeds configured that silence is undiagnosable — say why.
const feedStarved = computed(
    () => !feedPending.value && !!settings.value?.discoveryFeeds?.length && !feed.value?.length
);

const openSeries = (key: string) => navigateTo(`/series/${key}`);

// Click-to-add: the modal opens instantly with the entry's own details and resolves the real connector
// series inside itself (DiscoverAddModal) — the click never waits on a search.
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
