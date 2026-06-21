<template>
    <div title="Scope search to manga or comics">
        <UButtonGroup size="sm">
            <UButton
                :variant="mode === 'manga' ? 'solid' : 'outline'"
                :color="mode === 'manga' ? 'primary' : 'neutral'"
                :aria-pressed="mode === 'manga'"
                aria-label="Show manga"
                @click="setMode('manga')">
                Manga
            </UButton>
            <UButton
                :variant="mode === 'comic' ? 'solid' : 'outline'"
                :color="mode === 'comic' ? 'primary' : 'neutral'"
                :aria-pressed="mode === 'comic'"
                aria-label="Show comics"
                @click="setMode('comic')">
                Comics
            </UButton>
        </UButtonGroup>
    </div>
</template>

<script setup lang="ts">
const { mode, setMode } = useMediaMode();
</script>
