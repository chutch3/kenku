<template>
    <TrangaPage>
        <template #title>
            <h1 class="text-2xl">
                Series with Tag <UBadge variant="outline" color="primary" class="font-semibold text-xl ml-1">{{ tag }}</UBadge>
            </h1>
        </template>
        <LoadingPage :loading="status === 'pending'">
            <SeriesCardList
                :series="series"
                class="overflow-y-scroll h-full"
                @click="(m) => navigateTo(`/series/${m.key}?return=${$route.fullPath}`)" />
        </LoadingPage>
    </TrangaPage>
</template>

<script setup lang="ts">
const tag = useRoute().params.tag as string;
const { data: series, status } = await useApi('/v2/Series/WithTag/{Tag}', { path: { Tag: tag }, lazy: true, server: false });

useHead({ title: 'Tag search' });
</script>
