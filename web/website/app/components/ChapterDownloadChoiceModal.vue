<template>
    <UModal v-model:open="open" title="Choose a download" :description="`Pick which upload of ${chapterLabel} to download.`">
        <template #body>
            <div class="flex flex-col gap-2">
                <div
                    v-for="src in sources"
                    :key="src.key"
                    data-test="upload-option"
                    class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                    <SourceIcon v-bind="src" />
                    <div class="min-w-0 grow">
                        <p class="text-sm truncate">{{ src.scanGroup ?? src.mangaConnectorName }}</p>
                        <p class="text-xs text-muted">
                            {{ src.mangaConnectorName }}<span v-if="src.language"> · {{ src.language }}</span>
                        </p>
                    </div>
                    <UBadge v-if="src.useForDownload" color="success" variant="subtle" size="sm">selected</UBadge>
                    <UButton
                        :data-test="`pick-${src.key}`"
                        size="xs"
                        color="primary"
                        icon="i-lucide-cloud-download"
                        :loading="picking === src.key"
                        @click="pick(src.key)">
                        Download
                    </UButton>
                </div>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type ChapterSource = components['schemas']['ChapterSourceId'];

defineProps<{ sources: ChapterSource[]; chapterLabel: string }>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'pick', sourceKey: string): void }>();

const picking = ref<string | null>(null);
const pick = (sourceKey: string) => {
    picking.value = sourceKey;
    emit('pick', sourceKey);
};
</script>
