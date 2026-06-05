<template>
    <!--
      Kenku's mark: a hanko (vermillion seal-stamp) carrying a karasu-tengu —
      the crow-spirit the app is named for. Pure inline SVG, so there's no
      binary asset to ship and it stays crisp at any size and in any theme.
    -->
    <svg
        :width="size"
        :height="size"
        viewBox="0 0 100 100"
        role="img"
        :aria-label="label"
        class="kenku-mark"
        xmlns="http://www.w3.org/2000/svg">
        <title>{{ label }}</title>
        <!-- seal ring (brushed double stroke) -->
        <circle cx="50" cy="50" r="45" fill="none" stroke="currentColor" stroke-width="3.5" opacity="0.95" />
        <circle cx="50" cy="50" r="39.5" fill="none" stroke="currentColor" stroke-width="1.2" opacity="0.5" />
        <!-- karasu — crow-tengu in profile, facing the reader's right -->
        <path
            d="M36 71
               C29 61 27 48 36 38
               C39 35 36 30 40 27
               C43 31 44 29 48 26
               C51 24 56 25 60 30
               L75 36 L62 40 L74 44
               C60 49 55 50 50 47
               C47 55 43 63 41 71 Z"
            fill="currentColor" />
        <!-- carved eye: punches through to the paper/ink behind -->
        <circle cx="52.5" cy="37" r="2.4" :fill="eye" />
    </svg>
</template>

<script setup lang="ts">
withDefaults(
    defineProps<{
        size?: number | string;
        label?: string;
        /** Color shown through the carved eye; defaults to the page background. */
        eye?: string;
    }>(),
    { size: 40, label: 'Kenku', eye: 'var(--kenku-paper)' }
);
</script>

<style scoped>
.kenku-mark {
    color: var(--color-vermillion-500);
}
</style>
