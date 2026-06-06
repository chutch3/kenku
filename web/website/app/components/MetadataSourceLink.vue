<template>
    <UCard>
        <template #header>
            <div>
                <div class="flex items-center justify-between gap-2">
                    <h2 class="font-display text-lg font-semibold text-highlighted">Volume mapping</h2>
                    <UBadge :color="statusColor" variant="subtle">{{ statusLabel }}</UBadge>
                </div>
                <p class="text-xs text-muted">Match this series to its MangaDex entry to get correct volume &amp; chapter numbers.</p>
            </div>
        </template>

        <p v-if="needsLinking" class="text-sm text-muted mb-2">
            Volumes may be incomplete until this series is linked to the correct entry.
        </p>

        <div class="flex gap-2">
            <UInput v-model="query" placeholder="Search MangaDex by title" class="grow" @keydown.enter="search" />
            <UButton data-test="source-search-btn" icon="i-lucide-search" :loading="searching" @click="search">Search</UButton>
        </div>

        <p v-if="error" class="text-sm text-error mt-2">{{ error }}</p>
        <p v-else-if="searched && candidates.length === 0" class="text-sm text-muted mt-2">No candidates found.</p>

        <div v-if="candidates.length" class="flex flex-col gap-2 mt-2">
            <div
                v-for="c in candidates"
                :key="c.externalId ?? c.mangaDexId ?? ''"
                class="bg-elevated rounded-lg p-2 flex items-start justify-between gap-2">
                <div class="min-w-0">
                    <p class="font-medium truncate">{{ c.title }}</p>
                    <p class="text-xs text-muted">
                        <span v-if="c.author">{{ c.author }} · </span>
                        <span>{{ c.chapterCount }} ch · {{ Math.round((c.score ?? 0) * 100) }}%</span>
                    </p>
                    <p v-if="c.matchReasons?.length" class="text-xs text-muted">{{ c.matchReasons.join(' · ') }}</p>
                </div>
                <UButton
                    data-test="source-link-btn"
                    size="xs"
                    color="primary"
                    :loading="linkingId === (c.externalId ?? c.mangaDexId)"
                    @click="link(c)">
                    Link
                </UButton>
            </div>
        </div>
    </UCard>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';

type Candidate = components['schemas']['MetadataSourceCandidate'];

const props = defineProps<{ mangaId: string; seriesName?: string }>();
const { $api } = useNuxtApp();

const query = ref(props.seriesName ?? '');
const candidates = ref<Candidate[]>([]);
const searching = ref(false);
const searched = ref(false);
const error = ref<string | null>(null);
const linkingId = ref<string | null>(null);

const { data: source, refresh: refreshSource } = await useApi('/v2/Series/{MangaId}/metadataSource', {
    path: { MangaId: props.mangaId },
    key: `metadataSource-${props.mangaId}`,
    server: false,
});

const status = computed(() => source.value?.status ?? 'Unlinked');

const STATUS_LABELS: Record<string, string> = {
    Confirmed: 'Linked',
    AutoMatched: 'Auto-matched',
    Ambiguous: 'Needs review',
    NoMatch: 'Not matched',
    Unlinked: 'Not linked',
};
const statusLabel = computed(() => STATUS_LABELS[status.value] ?? status.value);

const statusColor = computed(() => {
    switch (status.value) {
        case 'Confirmed':
        case 'AutoMatched':
            return 'success';
        case 'Ambiguous':
        case 'NoMatch':
            return 'warning';
        default:
            return 'neutral';
    }
});

const needsLinking = computed(() => !['Confirmed', 'AutoMatched'].includes(status.value));

const search = async () => {
    if (!query.value.trim()) return;
    searching.value = true;
    error.value = null;
    try {
        candidates.value = await $api('/v2/Series/{MangaId}/metadataSource/candidates', {
            path: { MangaId: props.mangaId },
            query: { q: query.value, source: 'mangadex' },
        });
        searched.value = true;
    } catch {
        error.value = 'Search failed. Please try again.';
    } finally {
        searching.value = false;
    }
};

const link = async (candidate: Candidate) => {
    const externalId = candidate.externalId ?? candidate.mangaDexId;
    if (!externalId) return;
    linkingId.value = externalId;
    try {
        await $api('/v2/Series/{MangaId}/metadataSource', {
            method: 'PUT',
            path: { MangaId: props.mangaId },
            body: { sourceType: 'MangaDex', externalId },
        });
        await $api('/v2/Series/{MangaId}/metadataSource/refresh', { method: 'POST', path: { MangaId: props.mangaId } });
        candidates.value = [];
        searched.value = false;
        await refreshSource();
    } finally {
        linkingId.value = null;
    }
};
</script>
