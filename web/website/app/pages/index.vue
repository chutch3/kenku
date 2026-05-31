<template>
    <LoadingPage :loading="status === 'pending'">
        <SeriesCardList :series="series" class="min-md:mx-4 max-md:mx-0 mt-2" @click="(m) => navigateTo(`/series/${m.key}`)" />
    </LoadingPage>
</template>

<script setup lang="ts">
const { data: series, refresh, status } = await useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
onMounted(() => refresh());

useHead({ title: 'Tranga' });
</script>
