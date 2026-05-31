<template>
    <TrangaPage>
        <div class="flex flex-col items-center justify-center gap-10">
            <div class="flex flex-row max-sm:flex-col justify-evenly items-center">
                <SeriesCard v-if="series" :series="series" :expanded="true" />
                <USkeleton v-else class="max-w-[600px] w-full h-[350px]" />
                <UButton
                    icon="i-lucide-merge"
                    :class="[
                        reverse ? 'min-sm:-rotate-90 rotate-0' : 'min-sm:rotate-90 rotate-180',
                        'transition-transform duration-200 p-5 ml-6 mr-10 mt-10 mb-6',
                        'rounded-full',
                    ]"
                    size="xl"
                    variant="soft"
                    color="primary"
                    @click="reverse = !reverse" />
                <SeriesCard v-if="target" :series="target" :expanded="true" />
                <USkeleton v-else class="max-w-[600px] w-full h-[350px]" />
            </div>
            <p class="text-red-500 animate-pulse font-bold min-sm:text-3xl">This action is irreversible!</p>
            <UButton color="warning" variant="outline" class="w-fit" @click="merge">Merge</UButton>
        </div>
    </TrangaPage>
</template>

<script setup lang="ts">
const route = useRoute();
const targetId = route.params.targetId as string;
const mangaId = route.params.mangaId as string;
const { $api } = useNuxtApp();

const reverse = ref(false);
const { data: target } = await useApi('/v2/Series/{MangaId}', {
    path: { MangaId: targetId },
    key: FetchKeys.Series.Id(targetId),
    server: false,
});
const { data: series } = await useApi('/v2/Series/{MangaId}', {
    path: { MangaId: mangaId },
    key: FetchKeys.Series.Id(mangaId),
    server: false,
});

const merge = async () => {
    const from = reverse.value ? mangaId : targetId;
    const to = reverse.value == false ? targetId : mangaId;
    await $api('/v2/Series/{MangaIdFrom}/MergeInto/{MangaIdInto}', { method: 'POST', path: { MangaIdFrom: from, MangaIdInto: to } });
    navigateTo(`/series/${to}?return=${useRoute().fullPath}`);
};

useHead({ title: 'Confirm merge' });
</script>
