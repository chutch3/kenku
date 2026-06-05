<template>
    <UPageBody v-if="loading" class="h-[calc(100dvh-var(--ui-header-height))] flex items-center justify-center">
        <div class="flex flex-col items-center gap-4 -mt-16">
            <KenkuMark :size="56" class="animate-[pulse_1.6s_ease-in-out_infinite]" />
            <p class="font-mono text-xs uppercase tracking-[0.3em] text-muted">Summoning the hoard…</p>
        </div>
    </UPageBody>
    <UPageBody v-else v-bind="$props" class="mt-0 pb-0 px-4 h-full">
        <template v-for="(_, slotName) in $slots" #[slotName]>
            <slot :name="slotName" />
        </template>
    </UPageBody>
</template>

<script setup lang="ts">
import type { PageBodyProps } from '#ui/components/PageBody.vue';

export interface LoadingPageProps extends PageBodyProps {
    loading: boolean;
}

defineProps<LoadingPageProps>();
</script>
