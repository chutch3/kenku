<template>
    <UTooltip v-if="minutesLeft !== null" text="The indexer answered HTTP 429; Kenku skips it until the cooldown elapses.">
        <UBadge color="warning" variant="subtle" size="sm" icon="i-lucide-timer">
            Rate-limited · retry in {{ minutesLeft }}m
        </UBadge>
    </UTooltip>
</template>

<script setup lang="ts">
const props = defineProps<{ cooldownUntil?: string | null }>();

// Ticking clock so the countdown moves and the badge drops off when the cooldown elapses.
const now = ref(Date.now());
let tick: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
    tick = setInterval(() => {
        now.value = Date.now();
    }, 30_000);
});
onBeforeUnmount(() => {
    if (tick) clearInterval(tick);
});

const minutesLeft = computed(() => {
    if (!props.cooldownUntil) return null;
    const left = Date.parse(props.cooldownUntil) - now.value;
    return left > 0 ? Math.ceil(left / 60_000) : null;
});
</script>
