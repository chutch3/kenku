<template>
    <div class="flex items-start justify-between gap-3">
        <div>
            <h2 class="font-display text-lg font-semibold text-highlighted">{{ title }}</h2>
            <p v-if="subtitle" class="text-xs text-muted mt-0.5">{{ subtitle }}</p>
        </div>
        <slot />
    </div>
</template>

<script setup lang="ts">
defineProps<{ title: string; subtitle?: string }>();
</script>
