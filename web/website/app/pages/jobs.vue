<template>
    <KenkuPage title="Jobs">
        <template #actions>
            <UTooltip text="Reload" :kbds="['meta', 'R']">
                <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" loading-auto @click="reload" />
            </UTooltip>
        </template>
        <JobsList />
    </KenkuPage>
</template>

<script setup lang="ts">
const reload = () => refreshNuxtData(FetchKeys.Jobs.All);
defineShortcuts({ meta_r: { usingInput: true, handler: reload } });
</script>
