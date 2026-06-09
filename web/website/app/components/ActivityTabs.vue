<template>
    <div class="flex gap-1">
        <UButton :variant="active === 'operations' ? 'soft' : 'ghost'" color="secondary" icon="i-lucide-layers" size="sm" to="/queue">
            Operations
        </UButton>
        <UButton :variant="active === 'history' ? 'soft' : 'ghost'" color="secondary" icon="i-lucide-scroll-text" size="sm" to="/actions">
            History
        </UButton>
    </div>
</template>

<script setup lang="ts">
defineProps<{ active: 'operations' | 'history' }>();
</script>
