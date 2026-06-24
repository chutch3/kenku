<template>
    <!-- data-theme scopes the catalogue CSS to this element, so the vars below resolve to *this* theme.
         Mirroring the app's dark class previews it in the mode the user is actually in. -->
    <span
        :data-theme="themeId"
        :class="{ dark: isDark }"
        class="block rounded-md overflow-hidden ring-1 ring-inverted/10"
        :style="{ background: 'var(--ui-bg)' }">
        <span class="flex items-center gap-1.5 px-2 pt-2">
            <span class="h-1.5 flex-1 rounded-full" :style="{ background: 'var(--ui-text)', opacity: 0.65 }" />
            <span data-test="preview-accent" class="size-3 rounded-full shrink-0" :style="{ background: 'var(--ui-primary)' }" />
            <span class="size-3 rounded-full shrink-0" :style="{ background: 'var(--ui-secondary)' }" />
        </span>
        <span class="mx-2 my-2 block h-3 rounded" :style="{ background: 'var(--ui-bg-elevated)', border: '1px solid var(--ui-border)' }" />
    </span>
</template>

<script setup lang="ts">
defineProps<{ themeId: string }>();

const colorMode = useColorMode();
const isDark = computed(() => colorMode.value === 'dark');
</script>
