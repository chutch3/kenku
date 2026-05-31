<template>
    <img
        v-bind="$attrs"
        :src="displaySrc"
        :alt="alt"
        :loading="loading"
        decoding="async"
        @error="onError" />
</template>

<script setup lang="ts">
defineOptions({ inheritAttrs: false });

const props = withDefaults(
    defineProps<{
        src?: string | null;
        fallback?: string;
        alt?: string;
        loading?: 'eager' | 'lazy';
    }>(),
    {
        fallback: '/blahaj.png',
        alt: '',
        loading: 'lazy',
    }
);

const displaySrc = ref(props.src ?? props.fallback);

watch(
    () => props.src,
    (src) => {
        displaySrc.value = src ?? props.fallback;
    }
);

watch(
    () => props.fallback,
    (fallback) => {
        displaySrc.value ??= fallback;
    }
);

const onError = () => {
    if (displaySrc.value !== props.fallback) displaySrc.value = props.fallback;
};
</script>
