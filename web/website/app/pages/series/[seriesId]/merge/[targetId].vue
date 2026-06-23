<template>
    <KenkuPage>
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
    </KenkuPage>
</template>

<script setup lang="ts">
const route = useRoute();
const targetId = route.params.targetId as string;
const seriesId = route.params.seriesId as string;
const { $api } = useNuxtApp();

const reverse = ref(false);
const { data: target } = await useApi('/v2/Series/{SeriesId}', {
    path: { SeriesId: targetId },
    key: FetchKeys.Series.Id(targetId),
    server: false,
});
const { data: series } = await useApi('/v2/Series/{SeriesId}', {
    path: { SeriesId: seriesId },
    key: FetchKeys.Series.Id(seriesId),
    server: false,
});

const merge = async () => {
    const from = reverse.value ? seriesId : targetId;
    const to = reverse.value == false ? targetId : seriesId;
    await $api('/v2/Series/{SeriesIdFrom}/MergeInto/{SeriesIdInto}', { method: 'POST', path: { SeriesIdFrom: from, SeriesIdInto: to } });
    navigateTo(`/series/${to}?return=${useRoute().fullPath}`);
};

useHead({ title: 'Confirm merge' });
</script>
