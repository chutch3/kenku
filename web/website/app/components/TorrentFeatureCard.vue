<template>
    <UCard>
        <template #header>
            <SettingsHeader
                title="Torrents (experimental)"
                subtitle="Search and download via indexers + a torrent client. Off by default while the download and import flow is being designed. Changing this takes effect after a server restart." />
        </template>
        <USwitch :model-value="torrentEnabled" label="Enable torrent feature" @update:model-value="onToggle" />
    </UCard>
</template>

<script setup lang="ts">
const { torrentEnabled, setTorrentEnabled } = useSettings();
const toast = useToast();

const onToggle = async (enabled: boolean) => {
    await setTorrentEnabled(enabled);
    toast.add({
        title: enabled ? 'Torrents enabled' : 'Torrents disabled',
        description: 'Restart the server to apply.',
        icon: 'i-lucide-check',
        color: 'success',
    });
};
</script>
