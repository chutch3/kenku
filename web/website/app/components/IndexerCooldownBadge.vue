<template>
    <UTooltip v-if="minutesLeft !== null" text="The indexer answered HTTP 429; Kenku skips it until the cooldown elapses.">
        <UBadge color="warning" variant="subtle" size="sm" icon="i-lucide-timer">
            Rate-limited · retry in {{ minutesLeft }}m
        </UBadge>
    </UTooltip>
</template>

<script setup lang="ts">
const props = defineProps<{ cooldownUntil?: string | null }>();

const minutesLeft = computed(() => {
    if (!props.cooldownUntil) return null;
    const left = Date.parse(props.cooldownUntil) - Date.now();
    return left > 0 ? Math.ceil(left / 60_000) : null;
});
</script>
