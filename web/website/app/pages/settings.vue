<template>
    <KenkuPage title="Settings">
        <!-- API unreachable -->
        <div v-if="settingsStatus === 'error'" class="mt-2 rounded-lg ring-1 ring-warning/40 bg-warning/10 px-4 py-3">
            <p class="text-warning font-medium">Unable to connect to the Kenku API.</p>
            <p class="text-sm text-muted mt-1">
                NUXT_PUBLIC_OPEN_FETCH_API_BASE_URL: <code>{{ $config.public.openFetch.api.baseURL }}</code>
            </p>
        </div>

        <template v-else-if="settingsStatus === 'success'">
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
                        <IndexersCard />
                        <DownloadClientsCard />
                        <DownloadsCard />
                        <ReleaseSelectionCard />
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
const { settingsStatus } = useSettings();

const tabs = [
    { label: 'Library', icon: 'i-lucide-folder-tree', slot: 'library' as const },
    { label: 'Downloading', icon: 'i-lucide-download', slot: 'downloading' as const },
    { label: 'Discovery', icon: 'i-lucide-compass', slot: 'discovery' as const },
    { label: 'Notifications', icon: 'i-lucide-bell', slot: 'notifications' as const },
    { label: 'Maintenance', icon: 'i-lucide-wrench', slot: 'maintenance' as const },
];

useHead({ title: 'Settings' });
</script>
