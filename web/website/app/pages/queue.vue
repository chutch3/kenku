<template>
    <KenkuPage title="Activity">
        <template #actions>
            <UTooltip text="Reload" :kbds="['meta', 'R']">
                <UButton variant="soft" color="secondary" icon="i-lucide-refresh-ccw" loading-auto @click="reload" />
            </UTooltip>
        </template>
        <ActivityTabs active="operations" class="mb-3" />
        <QueueList />
    </KenkuPage>
</template>

<script setup lang="ts">
const toast = useToast();
const reload = async () => {
    await refreshNuxtData(FetchKeys.JobQueue.All);
    toast.add({ title: 'Queue refreshed', icon: 'i-lucide-check', color: 'neutral', duration: 1500 });
};
defineShortcuts({ meta_r: { usingInput: true, handler: reload } });
</script>
