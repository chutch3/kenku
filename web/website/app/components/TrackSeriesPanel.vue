<template>
    <UCard :ui="{ root: 'ring-2 ring-primary/30' }">
        <template #header>
            <div class="flex items-center gap-2">
                <UIcon name="i-lucide-bookmark-plus" class="text-primary size-5" />
                <h2 class="font-display text-lg font-semibold text-highlighted">Track this series</h2>
            </div>
        </template>

        <p class="text-sm text-muted mb-3">
            You're previewing this series. Add it to a library to start tracking it and downloading new chapters.
        </p>

        <template v-if="libraries && libraries.length">
            <LibrarySelect :manga-id="mangaId" :library-id="null" class="w-full" @library-changed="$emit('tracked')" />
            <p class="text-xs text-dimmed mt-2">
                Choosing a library adds the series to your hoard and begins downloading from your enabled sources.
            </p>
        </template>

        <div v-else-if="libraries" class="flex flex-col gap-3">
            <p class="text-sm text-muted">You don't have any libraries yet — a library is the folder where Kenku saves downloaded files.</p>
            <UButton :to="`/settings?return=${$route.fullPath}`" icon="i-lucide-folder-plus" color="primary" class="w-fit">
                Set up a library
            </UButton>
        </div>

        <USkeleton v-else class="h-9 w-full" />
    </UCard>
</template>

<script setup lang="ts">
defineProps<{ mangaId: string }>();
defineEmits<{ (e: 'tracked'): void }>();

const { data: libraries } = await useApi('/v2/FileLibrary', { server: false });
</script>
