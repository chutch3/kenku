<template>
    <SeriesDetailPage :series="series" title="Merge with">
        <USkeleton v-if="!series" class="w-full h-[350px]" />
        <SeriesCardList :series="allSeries" @click="(m) => navigateTo(`/series/${seriesId}/merge/${m.key}?return=${$route.fullPath}`)" />
    </SeriesDetailPage>
</template>

<script setup lang="ts">
const seriesId = useRoute().params.seriesId as string;

const { data: series } = await useApi('/v2/Series/{SeriesId}', {
    path: { SeriesId: seriesId },
    key: FetchKeys.Series.Id(seriesId),
    server: false,
});
const { data: allSeries } = await useApi('/v2/Series', { key: FetchKeys.Series.All, server: false });

useHead({ title: 'Merge Series' });
</script>
