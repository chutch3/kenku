<template>
    <UCard>
        <template #header>
            <SettingsHeader title="File libraries" subtitle="Folders where Kenku saves downloaded files." />
        </template>
        <FileLibraries />
        <template #footer>
            <UButton icon="i-lucide-folder-plus" color="primary" class="w-fit" @click="addLibraryModal.open()">Add library</UButton>
        </template>
    </UCard>
</template>

<script setup lang="ts">
import { LazyAddLibraryModal } from '#components';

const overlay = useOverlay();
const addLibraryModal = overlay.create(LazyAddLibraryModal);
</script>
