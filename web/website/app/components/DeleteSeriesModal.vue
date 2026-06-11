<template>
    <UModal
        v-model:open="open"
        :title="`Delete ${seriesName ?? 'series'}?`"
        description="Removes the series and its queued work from Kenku. Downloaded files stay on disk.">
        <template #body>
            <UAlert v-if="error" color="error" variant="subtle" icon="i-lucide-triangle-alert" title="Delete failed" :description="error" />
        </template>
        <template #footer>
            <div class="flex gap-2 w-full justify-end">
                <UButton color="neutral" variant="outline" :disabled="deleting" @click="open = false">Cancel</UButton>
                <UButton color="error" icon="i-lucide-trash" :loading="deleting" @click="remove">Delete</UButton>
            </div>
        </template>
    </UModal>
</template>

<script setup lang="ts">
const props = defineProps<{ mangaId: string; seriesName?: string }>();
const open = defineModel<boolean>('open', { default: false });
const emit = defineEmits<{ (e: 'deleted'): void }>();
const { $api } = useNuxtApp();

const deleting = ref(false);
const error = ref<string | null>(null);
const remove = async () => {
    deleting.value = true;
    error.value = null;
    try {
        await $api('/v2/Series/{MangaId}', { method: 'DELETE', path: { MangaId: props.mangaId } });
        emit('deleted');
        open.value = false;
    } catch (e) {
        error.value = e instanceof Error ? ((e as { data?: string }).data ?? e.message) : String(e);
    } finally {
        deleting.value = false;
    }
};
</script>
