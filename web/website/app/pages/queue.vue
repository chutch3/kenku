<template>
    <KenkuPage title="Queue">
        <template #actions>
            <UTooltip text="Reload" :kbds="['meta', 'R']">
                <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" loading-auto @click="reload" />
            </UTooltip>
        </template>
        <QueueList />
    </KenkuPage>
</template>

<script setup lang="ts">
const reload = () => refreshNuxtData(FetchKeys.JobQueue.All);
defineShortcuts({ meta_r: { usingInput: true, handler: reload } });
</script>
