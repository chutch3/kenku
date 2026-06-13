<template>
    <UModal v-model:open="open" title="Choose a download" description="The post offers more than one — pick which to fetch.">
        <template #body>
            <div v-if="options" class="flex flex-col gap-2">
                <p v-if="!options.options?.length" class="text-sm text-muted">
                    {{ options.reason ?? 'The post offers no downloads right now.' }}
                </p>
                <div
                    v-for="option in options.options"
                    :key="option.url ?? ''"
                    class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                    <div class="min-w-0 grow">
                        <p class="text-sm truncate">{{ option.label }}</p>
                        <p v-if="option.size" class="text-xs text-muted">{{ option.size }}</p>
                    </div>
                    <UButton
                        size="xs"
                        color="primary"
                        icon="i-lucide-cloud-download"
                        :loading="picking === option.url"
                        @click="pick(option)">
                        Fetch
                    </UButton>
                </div>
            </div>
            <USkeleton v-else class="h-16 w-full" />
        </template>
    </UModal>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type DownloadOptionsResponse = components['schemas']['DownloadOptionsResponse'];
type DownloadOption = components['schemas']['DownloadOption'];

const props = defineProps<{ sourceKey: string }>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'queued'): void }>();

const { $api } = useNuxtApp();
const toast = useToast();

// Resolved live on every open, so the choices reflect the post as it is right now.
const options = ref<DownloadOptionsResponse | null>(null);
watch(
    open,
    async (isOpen) => {
        if (!isOpen) return;
        options.value = null;
        options.value = await $api('/v2/Chapters/{ChapterSourceKey}/DownloadOptions', {
            path: { ChapterSourceKey: props.sourceKey },
        });
    },
    { immediate: true }
);

const picking = ref<string | null>(null);
const pick = async (option: DownloadOption) => {
    if (!option.url) return;
    picking.value = option.url;
    try {
        await $api('/v2/Chapters/{ChapterSourceKey}/Download', {
            method: 'POST',
            path: { ChapterSourceKey: props.sourceKey },
            body: { url: option.url },
        });
        toast.add({ title: `Queued ${option.label}`, icon: 'i-lucide-cloud-download', color: 'success' });
        emit('queued');
        open.value = false;
    } finally {
        picking.value = null;
    }
};
</script>
