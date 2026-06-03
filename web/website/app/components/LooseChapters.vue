<template>
    <div class="flex flex-col gap-2">
        <h3 class="font-semibold">Loose chapters</h3>
        <p v-if="!loose.length" class="text-sm text-muted">No loose chapters — every downloaded chapter has a volume.</p>
        <ul v-else class="flex flex-col gap-2">
            <li v-for="ch in loose" :key="ch.chapterId" class="flex items-center gap-2 text-sm">
                <span class="grow">Ch. {{ ch.chapterNumber }}</span>
                <UInput
                    v-model="volumeInput[ch.chapterNumber]"
                    type="number"
                    :min="1"
                    placeholder="Vol"
                    class="w-20" />
                <UButton
                    size="xs"
                    :loading="assigning === ch.chapterNumber"
                    :disabled="!volumeInput[ch.chapterNumber]"
                    @click="assign(ch.chapterNumber)">
                    Assign
                </UButton>
            </li>
        </ul>
    </div>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string }>();
const { $api } = useNuxtApp();

// Loose chapters are the ones the resolver couldn't place — the volumes endpoint returns them under
// `unassigned`. Assigning a volume here is a manual, locked assignment the resolver won't overwrite.
const { data: volumes, refresh } = await useApi('/v2/Series/{MangaId}/volumes', { path: { MangaId: props.mangaId }, server: false });

// The schema types ChapterId/ChapterNumber as nullable; in practice the API always sends them, so
// drop any malformed entries and narrow to non-null strings the template can key/index on.
const loose = computed(() =>
    (volumes.value?.unassigned ?? [])
        .filter((c) => c.chapterId && c.chapterNumber)
        .map((c) => ({ chapterId: c.chapterId as string, chapterNumber: c.chapterNumber as string })),
);

const volumeInput = reactive<Record<string, string | number>>({});
const assigning = ref<string | null>(null);

const assign = async (chapterNumber: string) => {
    const volume = Number(volumeInput[chapterNumber]);
    if (!Number.isInteger(volume) || volume < 1) return;

    assigning.value = chapterNumber;
    try {
        await $api('/v2/Series/{MangaId}/volumes/assignments', {
            method: 'POST',
            path: { MangaId: props.mangaId },
            body: { assignments: { [chapterNumber]: volume } },
        });
        delete volumeInput[chapterNumber];
        await refresh();
    } finally {
        assigning.value = null;
    }
};
</script>
