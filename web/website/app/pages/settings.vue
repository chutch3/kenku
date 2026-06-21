<template>
    <KenkuPage title="Settings">
        <!-- API unreachable -->
        <div v-if="settingsStatus === 'error' && !settingsData" class="mt-2 rounded-lg ring-1 ring-warning/40 bg-warning/10 px-4 py-3">
            <p class="text-warning font-medium">Unable to connect to the Kenku API.</p>
            <p class="text-sm text-muted mt-1">
                NUXT_PUBLIC_OPEN_FETCH_API_BASE_URL: <code>{{ $config.public.openFetch.api.baseURL }}</code>
            </p>
        </div>

        <!-- Gate on data presence, not fetch status: a background refetch (e.g. after saving a genre)
             keeps `settingsData` populated, so the tabs stay mounted instead of flashing the spinner. -->
        <template v-else-if="settingsData">
            <SettingsStatsStrip />

            <UTabs :items="tabs" variant="link" color="primary" class="w-full" :ui="{ list: 'mb-4' }">
                <!-- LIBRARY -->
                <template #library>
                    <div class="flex flex-col gap-4 max-w-3xl">
                        <FileLibrariesCard />
                        <LibraryServersCard />
                    </div>
                </template>

                <!-- DOWNLOADING -->
                <template #downloading>
                    <div class="flex flex-col gap-4 max-w-3xl">
                        <UCard>
                            <template #header>
                                <SettingsHeader
                                    title="Sources"
                                    subtitle="Sites Kenku searches and downloads from. Disabled sources are skipped everywhere, including All-sources search." />
                            </template>
                            <SourcesTable />
                            <DownloadLanguageField class="mt-4" />
                        </UCard>
                        <TorrentFeatureCard />
                        <!-- Show the torrent config when the feature is on OR there's existing config to
                             manage — so a clean install isn't cluttered, but a user's setup is never hidden. -->
                        <template v-if="showTorrentConfig">
                            <IndexersCard />
                            <DownloadClientsCard />
                            <ReleaseSelectionCard />
                        </template>
                        <DownloadsCard />
                        <MetronCard />
                    </div>
                </template>

                <!-- DISCOVERY -->
                <template #discovery>
                    <UCard class="max-w-3xl">
                        <template #header>
                            <SettingsHeader title="Discovery" subtitle="What shows up on the Discover page." />
                        </template>
                        <DiscoveryGenresField />
                    </UCard>
                </template>

                <!-- NOTIFICATIONS -->
                <template #notifications>
                    <UCard class="max-w-3xl">
                        <template #header>
                            <SettingsHeader title="Notifications" subtitle="Get pinged when Kenku downloads new chapters." />
                        </template>
                        <NotificationConnectors />
                        <template #footer>
                            <NotificationAddButtons />
                        </template>
                    </UCard>
                </template>

                <!-- MAINTENANCE -->
                <template #maintenance>
                    <UCard class="max-w-3xl">
                        <template #header>
                            <SettingsHeader title="Maintenance" subtitle="Housekeeping for the database, files, and the job queue." />
                        </template>
                        <MaintenancePanel />
                    </UCard>
                </template>
            </UTabs>
        </template>

        <!-- loading -->
        <div v-else class="flex justify-center py-24">
            <KenkuMark :size="48" class="animate-[pulse_1.6s_ease-in-out_infinite]" />
        </div>
    </KenkuPage>
</template>

<script setup lang="ts">
const { settingsStatus, settingsData, torrentEnabled, syncedIndexers, downloadClients } = useSettings();

// Existing indexer/client config means the user already uses torrents, so keep their config reachable
// even with the feature toggled off; otherwise hide the cards until they enable it.
const showTorrentConfig = computed(
    () => torrentEnabled.value || syncedIndexers.value.length > 0 || downloadClients.value.length > 0
);

const tabs = [
    { label: 'Library', icon: 'i-lucide-folder-tree', slot: 'library' as const },
    { label: 'Downloading', icon: 'i-lucide-download', slot: 'downloading' as const },
    { label: 'Discovery', icon: 'i-lucide-compass', slot: 'discovery' as const },
    { label: 'Notifications', icon: 'i-lucide-bell', slot: 'notifications' as const },
    { label: 'Maintenance', icon: 'i-lucide-wrench', slot: 'maintenance' as const },
];

useHead({ title: 'Settings' });
</script>
