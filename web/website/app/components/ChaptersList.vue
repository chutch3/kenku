<template>
    <div class="w-full pt-2">
        <div class="flex min-2xl:flex-row max-2xl:flex-col gap-2 justify-around items-center mb-2">
            <div class="grow-1 basis-0 flex gap-2 flex-row max-sm:flex-wrap max-2xl:order-2 items-center">
                <p class="text-dimmed">{{ data?.totalCount }} {{ kind === 'comic' ? 'issues' : 'chapters' }}</p>
                <UFieldGroup>
                    <UInput v-model="filter.name" placeholder="Name" />
                    <UButton icon="i-lucide-rotate-ccw" variant="outline" size="xs" aria-label="Clear name filter" @click="filter.name = undefined" />
                </UFieldGroup>
            </div>
            <UPagination
                :default-page="pagination.pageIndex + 1"
                :items-per-page="pagination.pageSize"
                :total="data?.totalCount ?? 0"
                class="flex justify-center grow-1 basis-0 max-2xl:order-1"
                @update:page="(p) => (pagination.pageIndex = p - 1)" />
            <div class="grow-1 basis-0 flex gap-2 flex-row max-sm:flex-wrap max-2xl:order-3">
                <UFieldGroup>
                    <UTooltip text="Downloaded">
                        <UButton
                            :icon="
                                filter.downloaded
                                    ? 'i-lucide-cloud-check'
                                    : filter.downloaded === false
                                      ? 'i-lucide-cloud-alert'
                                      : 'i-lucide-badge-question-mark'
                            "
                            variant="outline"
                            color="neutral"
                            aria-label="Toggle downloaded filter"
                            @click="filter.downloaded = !filter.downloaded" />
                    </UTooltip>
                    <UButton icon="i-lucide-rotate-ccw" variant="outline" size="xs" aria-label="Clear downloaded filter" @click="filter.downloaded = undefined" />
                </UFieldGroup>
                <UFieldGroup v-if="kind !== 'comic'">
                    <UInputNumber v-model="filter.volumeNumber" placeholder="Vol" class="w-30" />
                    <UButton icon="i-lucide-rotate-ccw" variant="outline" size="xs" aria-label="Clear volume filter" @click="filter.volumeNumber = undefined" />
                </UFieldGroup>
                <UFieldGroup>
                    <UInput v-model="filter.chapterNumber" :placeholder="kind === 'comic' ? '#' : 'Ch'" class="w-30" />
                    <UButton icon="i-lucide-rotate-ccw" variant="outline" size="xs" aria-label="Clear chapter filter" @click="filter.chapterNumber = undefined" />
                </UFieldGroup>
            </div>
        </div>
        <UPageList class="gap-2 overflow-y-scroll px-[1px] py-[1px]">
            <UPageCard
                v-for="chapter in data?.data"
                :id="chapter.key"
                :key="chapter.key"
                orientation="horizontal"
                :ui="{ container: 'p-2 sm:p-2' }"
                :class="[$route.hash.substring(1) == chapter.key ? 'animate-[flash_0.75s_ease_0.5s]' : '']">
                <template #title>
                    <p class="text-primary">{{ chapter.title }}</p>
                    <p class="text-secondary">
                        <span v-if="chapter.volume" class="mr-1">Vol. {{ chapter.volume }}</span>
                        <span class="inline">{{ kind === 'comic' ? `#${chapter.chapterNumber}` : `Ch. ${chapter.chapterNumber}` }}</span>
                    </p>
                </template>
                <template #description>
                    <p>{{ chapter.fileName }}</p>
                </template>
                <template #default>
                    <div class="flex flex-row gap-2 w-full items-center">
                        <UTooltip :text="chapter.downloaded ? 'Downloaded' : 'Not downloaded'">
                            <UIcon
                                :name="chapter.downloaded ? 'i-lucide-cloud-check' : 'i-lucide-cloud-alert'"
                                size="20"
                                :class="chapter.downloaded ? 'text-success' : 'text-dimmed'" />
                        </UTooltip>
                        <!-- One upload: a plain toggle. -->
                        <div
                            v-if="chapter.sourceIds.length === 1"
                            class="bg-elevated p-1 rounded-lg w-fit flex items-center justify-center gap-2">
                            <SourceIcon v-bind="chapter.sourceIds[0]!" />
                            <UTooltip :text="chapter.sourceIds[0]!.useForDownload ? 'Stop downloading from this website' : 'Download from this website'">
                                <UButton
                                    :data-test="`download-${chapter.sourceIds[0]!.key}`"
                                    :icon="chapter.sourceIds[0]!.useForDownload ? 'i-lucide-cloud-off' : 'i-lucide-cloud-download'"
                                    variant="ghost"
                                    loading-auto
                                    :aria-label="chapter.sourceIds[0]!.useForDownload ? 'Stop downloading' : 'Download'"
                                    @click="setDownloadFromSource(chapter.sourceIds[0]!.key, !chapter.sourceIds[0]!.useForDownload)" />
                            </UTooltip>
                        </div>

                        <!-- Several uploads (e.g. MangaDex scan groups): pick which one to download. -->
                        <UButton
                            v-else-if="chapter.sourceIds.length > 1"
                            data-test="choose-download"
                            icon="i-lucide-list-checks"
                            size="xs"
                            variant="soft"
                            color="secondary"
                            @click="openChooser(chapter)">
                            Choose download ({{ chapter.sourceIds.length }})
                        </UButton>

                        <!-- TODO: Not implemented yet -->
                        <UButton variant="outline" color="secondary" class="ml-auto" disabled>Force (re)download</UButton>
                    </div>
                </template>
            </UPageCard>
        </UPageList>

        <ChapterDownloadChoiceModal
            v-if="chooserChapter"
            v-model:open="chooserOpen"
            :sources="chooserChapter.sourceIds"
            :chapter-label="chooserLabel"
            @pick="onPick" />
    </div>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type ChapterFilterRecord = components['schemas']['ChapterFilterRecord'];
type Chapter = components['schemas']['Chapter'];

const filter = ref<Partial<ChapterFilterRecord>>({});

const pagination = ref({ pageIndex: 0, pageSize: 10 });

export interface ChaptersListProps {
    mangaId: string;
    kind?: SeriesKind;
}
const props = defineProps<ChaptersListProps>();
const { $api } = useNuxtApp();

const { data, refresh } = useAsyncData(
    FetchKeys.Chapters.Series(props.mangaId),
    () =>
        $api('/v2/Chapters/Series/{MangaId}', {
            method: 'POST',
            query: { page: pagination.value.pageIndex + 1, pageSize: pagination.value.pageSize },
            path: { MangaId: props.mangaId },
            body: filter.value,
        }),
    { watch: [pagination.value, filter.value], lazy: true, server: false }
);

// Download a specific upload by its source key — unambiguous when one chapter has several uploads
// (e.g. MangaDex scan groups), where a connector-name toggle could not tell them apart.
const setDownloadFromSource = async (sourceKey: string, requested: boolean) => {
    await $api('/v2/Chapters/Source/{ChapterSourceKey}/Download/{IsRequested}', {
        method: 'PATCH',
        path: { ChapterSourceKey: sourceKey, IsRequested: requested },
    });
    await refresh();
};

const chooserChapter = ref<Chapter | null>(null);
const chooserOpen = ref(false);
const chooserLabel = computed(() => {
    const c = chooserChapter.value;
    if (!c) return '';
    const vol = c.volume ? `Vol. ${c.volume} ` : '';
    return `${vol}${props.kind === 'comic' ? `#${c.chapterNumber}` : `Ch. ${c.chapterNumber}`}`;
});
const openChooser = (chapter: Chapter) => {
    chooserChapter.value = chapter;
    chooserOpen.value = true;
};
const onPick = async (sourceKey: string) => {
    chooserOpen.value = false;
    await setDownloadFromSource(sourceKey, true);
};
</script>
