<template>
    <UModal
        v-model:open="open"
        :title="`Delete ${seriesName ?? 'series'}?`"
        description="Removes the series and its queued work from Kenku. Downloaded files stay on disk.">
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
const remove = async () => {
    deleting.value = true;
    try {
        await $api('/v2/Series/{MangaId}', { method: 'DELETE', path: { MangaId: props.mangaId } });
        emit('deleted');
        open.value = false;
    } finally {
        deleting.value = false;
    }
};
</script>
