<template>
    <TrangaPage>
        <template #title>
            <h1 class="text-2xl">
                Series with Author <UBadge variant="outline" color="neutral" class="font-semibold text-xl ml-1">{{ author?.name }}</UBadge>
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
const authorId = useRoute().params.authorId as string;

const { data: author } = await useApi('/v2/Author/{AuthorId}', { path: { AuthorId: authorId }, server: false });
const { data: series, status } = await useApi('/v2/Series/WithAuthorId/{AuthorId}', {
    path: { AuthorId: authorId },
    lazy: true,
    server: false,
});

useHead({ title: 'Author Search' });
</script>
