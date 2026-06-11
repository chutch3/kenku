<template>
    <img v-bind="$attrs" :src="displaySrc" :alt="alt" :loading="loading" decoding="async" @error="onError" />
</template>

<script setup lang="ts">
defineOptions({ inheritAttrs: false });

const props = withDefaults(
    defineProps<{
        src?: string | null;
        /** Tried in order after src fails, before the terminal fallback. */
        fallbacks?: (string | null | undefined)[];
        fallback?: string;
        alt?: string;
        loading?: 'eager' | 'lazy';
    }>(),
    {
        src: null,
        fallbacks: () => [],
        fallback: '/kenku.svg',
        alt: '',
        loading: 'lazy',
    }
);

const chain = computed(() => [props.src, ...props.fallbacks, props.fallback].filter((s): s is string => !!s));
const failed = ref(0);
watch(chain, () => {
    failed.value = 0;
});
const displaySrc = computed(() => chain.value[Math.min(failed.value, chain.value.length - 1)]);

const onError = () => {
    if (failed.value < chain.value.length - 1) failed.value++;
};
</script>
