<template>
    <span v-if="version" class="font-mono text-xs text-dimmed" :title="`commit ${version.commit} · built ${version.builtAt}`">
        {{ version.version }}
    </span>
</template>

<script setup lang="ts">
const { data: version } = useApi('/v2/Version', { key: FetchKeys.Version, server: false });
</script>
