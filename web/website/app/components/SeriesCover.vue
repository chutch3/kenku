<template>
    <div
        class="kenku-cover group/cover relative max-sm:w-[var(--mangacover-width-sm)] max-sm:h-[var(--mangacover-height-sm)] w-(--mangacover-width) h-(--mangacover-height) rounded-lg overflow-clip ring-1 ring-default">
        <FallbackImage
            :src="coverSources[0]"
            :fallbacks="coverSources.slice(1)"
            :alt="`${series.name} cover`"
            class="w-full h-full object-cover transition-transform duration-500 group-hover/cover:scale-105" />
        <!-- Manga-cover scrim: art stays visible, title sits in an ink wash. -->
        <div
            v-if="blur"
            class="absolute inset-x-0 bottom-0 flex flex-col justify-end pt-12 pb-3 px-3 bg-gradient-to-t from-black/90 via-black/55 to-transparent">
            <span class="h-px w-8 bg-vermillion-500 mb-2 shadow-[0_0_8px_var(--color-vermillion-500)]" />
            <p
                class="font-display max-sm:text-sm text-lg/tight font-semibold text-white line-clamp-3 [text-shadow:0_1px_8px_rgba(0,0,0,0.6)]">
                {{ series?.name }}
            </p>
            <slot name="footer" />
        </div>
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Series = components['schemas']['Series'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const props = defineProps<{ series: Series | MinimalSeries; blur?: boolean }>();
const config = useRuntimeConfig();
// Tracked series prefer Kenku's cached copy — source cover URLs rot (rotating CDN hosts) while the
// cache survives. Search results aren't cached yet, so they hotlink the source first.
const coverSources = computed(() => {
    const m = props.series as Series;
    const cacheUrl = `${config.public.openFetch.api.baseURL}v2/Series/${props.series.key}/Cover/Medium`;
    const sourceUrl = m.coverUrl && m.coverUrl.length > 0 ? m.coverUrl : null;
    return (props.series.fileLibraryId ? [cacheUrl, sourceUrl] : [sourceUrl, cacheUrl]).filter((s): s is string => !!s);
});
</script>
