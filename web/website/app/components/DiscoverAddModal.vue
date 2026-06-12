<template>
    <UModal v-model:open="open" :title="entry.title ?? 'Add series'">
        <template #body>
            <AddSeriesForm v-if="match" :series="match" @added="onFormAdded" />
            <div v-else class="flex flex-col gap-3">
                <div class="flex gap-4">
                    <FallbackImage :src="entry.coverUrl" :alt="entry.title ?? ''" class="w-24 rounded-md shrink-0 self-start" />
                    <div class="flex flex-col gap-2 min-w-0">
                        <p v-if="failed" class="text-sm text-muted">
                            Couldn't confidently match this on your sources — the full search lets you pick from every result.
                        </p>
                        <template v-else>
                            <p v-if="entry.blurb" class="text-sm text-muted line-clamp-4">{{ entry.blurb }}</p>
                            <p class="text-sm flex items-center gap-1.5">
                                <UIcon name="i-lucide-loader-circle" class="animate-spin text-secondary" />
                                Finding it on your sources…
                            </p>
                        </template>
                    </div>
                </div>
                <UButton v-if="failed" icon="i-lucide-search" color="primary" class="w-fit" @click="openSearch">Search instead</UButton>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Entry = components['schemas']['DiscoveryEntry'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const props = defineProps<{
    entry: Entry;
    /** Connector that owns the entry's URL (e.g. GetComics) — resolved exactly by URL when set. */
    source?: string;
}>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'added', series: MinimalSeries, payload: { libraryId: string; download: boolean }): void }>();

const { searchByUrl, searchByConnector } = useSeriesSearch();

// Cap the lookup well below the patience threshold — past it, the full search page is the better tool.
const RESOLVE_TIMEOUT_MS = 8000;

const match = ref<MinimalSeries | null>(null);
const failed = ref(false);

// The modal opens instantly with the entry's own details; the lookup fills it in. Source-owned
// entries resolve exactly by URL; AniList entries search manga scrapers only — torrent indexers
// are skipped (slow, and every click would spend indexer quota).
watch(
    open,
    async (isOpen) => {
        if (!isOpen) return;
        const target = props.entry;
        match.value = null;
        failed.value = false;
        try {
            const found =
                props.source && props.entry.url
                    ? await searchByUrl(props.entry.url, { timeoutMs: RESOLVE_TIMEOUT_MS })
                    : ((await searchByConnector('Global', props.entry.title ?? '', {
                          contentType: 'Manga',
                          includeTorrents: false,
                          timeoutMs: RESOLVE_TIMEOUT_MS,
                      })).find((s) => normalizeTitle(s.name) === normalizeTitle(props.entry.title)) ?? null);
            // A close-and-reopen on another card mid-lookup means this result no longer owns the
            // modal — applying it would put the wrong series behind the Add buttons.
            if (props.entry !== target || !open.value) return;
            if (found) match.value = found;
            else failed.value = true;
        } catch {
            if (props.entry === target && open.value) failed.value = true;
        }
    },
    { immediate: true }
);

const openSearch = () => {
    void navigateTo(`/search?q=${encodeURIComponent(props.entry.title ?? '')}${props.source ? `&source=${props.source}` : ''}`);
};

const onFormAdded = (payload: { libraryId: string; download: boolean }) => {
    emit('added', match.value!, payload);
    open.value = false;
};
</script>
