<template>
    <ClientOnly>
        <LoadingPage :loading="status === 'pending'">
            <div
                v-if="!series || series.length === 0"
                class="reveal flex flex-col items-center justify-center gap-5 h-[calc(100dvh-var(--ui-header-height)-2rem)] text-center px-6">
                <KenkuMark :size="76" class="opacity-90" />
                <div class="flex flex-col gap-1">
                    <p class="font-display text-2xl text-highlighted">The hoard is empty</p>
                    <p class="text-muted max-w-sm">The kenku has nothing to mimic yet. Track your first series to begin the collection.</p>
                </div>
                <UButton to="/search" color="primary" icon="i-lucide-plus" size="lg">Add a series</UButton>
            </div>
            <SeriesCardList
                v-else
                :series="series"
                class="min-md:mx-4 max-md:mx-0 mt-2"
                @click="(m) => navigateTo(`/series/${m.key}`)" />
        </LoadingPage>
        <template #fallback>
            <LoadingPage :loading="true" />
        </template>
    </ClientOnly>
</template>

<script setup lang="ts">
const { data: series, refresh, status } = await useApi('/v2/Series', { key: FetchKeys.Series.All, lazy: true, server: false });
onMounted(() => refresh());

useHead({ title: 'Kenku' });
</script>
