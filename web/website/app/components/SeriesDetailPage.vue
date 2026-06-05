<template>
    <KenkuPage v-bind="$props">
        <template #left>
            <div class="flex flex-col gap-2">
                <SeriesCover v-if="series" :series="series" class="self-center" />
                <USkeleton v-else class="w-[240px] h-[350px]" />
                <p v-if="series" class="font-display font-semibold text-2xl/snug text-highlighted">
                    {{ series.name }}
                    <SourceIcon v-for="m in series.sourceIds" v-bind="m" :key="m.key" />
                </p>
                <div v-if="series" class="flex flex-wrap items-center gap-2">
                    <SeriesStatusBadge :series="series" type="track" />
                    <SeriesStatusBadge :series="series" type="release" />
                </div>
                <USkeleton v-else as="p" class="h-20 w-full" />
                <div v-if="series" class="flex flex-row gap-1 flex-wrap">
                    <UBadge v-for="author in series.authors" :key="author.key" variant="outline" color="neutral"
                        ><NuxtLink :to="`/series/author/${author.key}?return=${$route.fullPath}`">{{ author.name }}</NuxtLink></UBadge
                    >
                    <UBadge v-for="tag in series.tags" :key="tag" variant="outline" color="primary"
                        ><NuxtLink :to="`/series/tag/${tag}?return=${$route.fullPath}`">{{ tag }}</NuxtLink></UBadge
                    >
                    <NuxtLink v-for="link in series.links" :key="link.key" :to="link.url" external no-prefetch>
                        <UBadge variant="outline" color="secondary">{{ link.provider }}</UBadge>
                    </NuxtLink>
                </div>
                <USkeleton v-else class="w-full h-lh" />
                <MDC v-if="series" :value="series.description" class="min-h-lh grow" />
                <USkeleton v-else class="w-full h-30" />
            </div>
        </template>
        <template #actions>
            <slot name="actions" />
        </template>
        <slot />
    </KenkuPage>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
import KenkuPage, { type PageProps } from '~/components/KenkuPage.vue';
type Series = components['schemas']['Series'];

export interface SeriesDetailPageProps extends PageProps {
    series?: Series | null;
}

defineProps<SeriesDetailPageProps>();
</script>
