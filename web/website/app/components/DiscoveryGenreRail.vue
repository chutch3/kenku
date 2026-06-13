<template>
    <DiscoveryRail
        :title="genre"
        subtitle="trending in genre"
        :entries="entries"
        :library="library"
        :exclude="exclude"
        @pick="(e) => emit('pick', e)"
        @open="(k) => emit('open', k)" />
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Entry = components['schemas']['DiscoveryEntry'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const props = defineProps<{ genre: string; library?: MinimalSeries[] | null; exclude?: string[] }>();
const emit = defineEmits<{
    (e: 'pick', entry: Entry): void;
    (e: 'open', seriesKey: string): void;
    (e: 'content', genre: string, hasContent: boolean): void;
}>();

// One rail per configured genre, rendered in a list — so the fetch must not await.
const { data: entries } = useApi('/v2/Discover/Manga/Genre/{Genre}', {
    path: { Genre: props.genre },
    key: FetchKeys.Discover.Genre(props.genre),
    lazy: true,
    server: false,
});

// The page can't see a self-fetched rail's contents; report whether this one shows anything (after
// the cross-rail exclude) so it can hide the section header and surface the empty state correctly.
watch(entries, (loaded) => {
    if (loaded == null) return; // still loading — leave the page's optimistic default
    const excluded = new Set(props.exclude ?? []);
    emit('content', props.genre, loaded.some((e) => !excluded.has(normalizeTitle(e.title))));
});
</script>
