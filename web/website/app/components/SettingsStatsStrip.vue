<template>
    <div v-if="stats" class="flex flex-wrap gap-2 mb-5">
        <div v-for="(value, name) in stats" :key="name" class="flex items-baseline gap-1.5 rounded-lg bg-elevated px-3 py-1.5">
            <span class="font-display text-lg text-highlighted">{{ value }}</span>
            <span class="text-xs text-muted">{{ deCamel(name) }}</span>
        </div>
    </div>
</template>

<script setup lang="ts">
const { stats } = useSettings();
</script>
