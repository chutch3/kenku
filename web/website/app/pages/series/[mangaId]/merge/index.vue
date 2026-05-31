<template>
    <SeriesDetailPage :series="series" title="Merge with">
        <USkeleton v-if="!series" class="w-full h-[350px]" />
        <SeriesCardList :series="allSeries" @click="(m) => navigateTo(`/series/${mangaId}/merge/${m.key}?return=${$route.fullPath}`)" />
    </SeriesDetailPage>
</template>

<script setup lang="ts">
const mangaId = useRoute().params.mangaId as string;

const { data: series } = await useApi('/v2/Series/{MangaId}', {
    path: { MangaId: mangaId },
    key: FetchKeys.Series.Id(mangaId),
    server: false,
});
const { data: allSeries } = await useApi('/v2/Series', { key: FetchKeys.Series.All, server: false });

useHead({ title: 'Merge Series' });
</script>
