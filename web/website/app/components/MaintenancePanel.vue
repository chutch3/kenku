<template>
    <div class="flex flex-col gap-4">
        <div class="flex gap-2 flex-wrap">
            <UButton icon="i-lucide-database" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/CleanupNoDownloadManga', 'Removed series with no download sources', FetchKeys.Series.All)">
                Clean database
            </UButton>
            <UButton icon="i-lucide-captions-off" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/CleanupActions', 'Action log cleared')">
                Clean actions
            </UButton>
            <UButton icon="i-lucide-file-x" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/CleanupOrphanedFiles', 'Orphaned-file cleanup queued')">
                Clean orphaned files
            </UButton>
            <UButton icon="i-lucide-list-ordered" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/ResolveMissingVolumes', 'Volume resolution queued')">
                Resolve missing volumes
            </UButton>
            <UButton icon="i-lucide-file-pen" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/SyncChapterFileNames', 'File renames queued')">
                Sync file names
            </UButton>
            <UButton icon="i-lucide-eraser" variant="soft" loading-auto class="w-fit" @click="run('/v2/Maintenance/PruneCompletedJobs', 'Job pruning queued')">
                Prune completed jobs
            </UButton>
        </div>
        <div class="flex items-end gap-2">
            <UFormField label="Completed-job retention (days)" description="Succeeded and cancelled jobs older than this are pruned; jobs needing attention are always kept.">
                <UInputNumber v-model="retentionDays" :min="1" class="w-40" />
            </UFormField>
            <UButton variant="soft" loading-auto @click="saveRetention">Save retention</UButton>
        </div>
    </div>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const run = async (path: Parameters<typeof $api>[0], done: string, refreshKey?: string) => {
    await $api(path, { method: 'POST' });
    toast.add({ title: done, icon: 'i-lucide-check', color: 'success' });
    if (refreshKey) await refreshNuxtData(refreshKey);
};

const { data: currentRetention } = useApi('/v2/Settings/CompletedJobRetentionDays', { key: FetchKeys.Settings.JobRetention, server: false });
const retentionDays = ref<number>(3);
watch(currentRetention, (v) => { if (v != null) retentionDays.value = v; }, { immediate: true });

const saveRetention = async () => {
    await $api('/v2/Settings/CompletedJobRetentionDays/{days}', { method: 'PATCH', path: { days: retentionDays.value } });
    toast.add({ title: `Completed jobs kept for ${retentionDays.value} days`, icon: 'i-lucide-check', color: 'success' });
};
</script>
