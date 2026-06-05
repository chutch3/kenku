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
            <!-- Stats strip -->
            <div class="flex flex-wrap gap-2 mb-5">
                <div v-for="(value, name) in stats" :key="name" class="flex items-baseline gap-1.5 rounded-lg bg-elevated px-3 py-1.5">
                    <span class="font-display text-lg text-highlighted">{{ value }}</span>
                    <span class="text-xs text-muted">{{ deCamel(name) }}</span>
                </div>
            </div>

            <UTabs :items="tabs" variant="link" color="primary" class="w-full" :ui="{ list: 'mb-4' }">
                <!-- LIBRARY -->
                <template #library>
                    <div class="flex flex-col gap-4 max-w-3xl">
                        <UCard>
                            <template #header>
                                <SettingsHeader title="File libraries" subtitle="Folders where Kenku saves downloaded files." />
                            </template>
                            <FileLibraries />
                            <template #footer>
                                <UButton icon="i-lucide-folder-plus" color="primary" class="w-fit" @click="addLibraryModal.open()">
                                    Add library
                                </UButton>
                            </template>
                        </UCard>

                        <UCard>
                            <template #header>
                                <SettingsHeader
                                    title="Library servers"
                                    subtitle="Mirror your collection into a reader like Komga or Kavita." />
                            </template>
                            <div class="flex flex-col gap-2">
                                <div
                                    v-for="srv in [
                                        { name: 'Komga', connected: !!komgaConnected, onClick: onKomgaClick },
                                        { name: 'Kavita', connected: !!kavitaConnected, onClick: onKavitaClick },
                                    ]"
                                    :key="srv.name"
                                    class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
                                    <span class="grow font-medium">{{ srv.name }}</span>
                                    <UBadge :color="srv.connected ? 'success' : 'neutral'" variant="subtle">
                                        {{ srv.connected ? 'Connected' : 'Not connected' }}
                                    </UBadge>
                                    <UButton
                                        :icon="srv.connected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                        :color="srv.connected ? 'neutral' : 'primary'"
                                        variant="soft"
                                        size="sm"
                                        @click="srv.onClick">
                                        {{ srv.connected ? 'Disconnect' : 'Connect' }}
                                    </UButton>
                                </div>
                            </div>
                        </UCard>
                    </div>
                </template>

                <!-- DOWNLOADING -->
                <template #downloading>
                    <div class="flex flex-col gap-4 max-w-3xl">
                        <UCard>
                            <template #header>
                                <SettingsHeader
                                    title="Indexers via Prowlarr"
                                    subtitle="Kenku appears as a Mylar app in Prowlarr, which syncs your comic indexers automatically.">
                                    <UBadge :color="syncedIndexers.length ? 'success' : 'neutral'" variant="subtle">
                                        {{ syncedIndexers.length }} synced
                                    </UBadge>
                                </SettingsHeader>
                            </template>
                            <div class="rounded-lg bg-elevated ring-1 ring-default px-3 py-2 mb-3">
                                <p class="text-xs text-muted">
                                    In Prowlarr: <b class="text-toned">Settings → Apps → Add → Mylar</b>, then paste the URL and API key
                                    below.
                                </p>
                            </div>
                            <UFormField label="Kenku base URL (Mylar server)">
                                <UInput :model-value="baseUrl" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                                    <template #trailing>
                                        <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" @click="copy(baseUrl)" />
                                    </template>
                                </UInput>
                            </UFormField>
                            <UFormField label="API key" class="mt-2">
                                <UInput :model-value="apiKey" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                                    <template #trailing>
                                        <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" @click="copy(apiKey)" />
                                    </template>
                                </UInput>
                            </UFormField>
                            <div class="mt-3">
                                <UButton icon="i-lucide-refresh-cw" variant="soft" class="w-fit" loading-auto @click="regenerateApiKey">
                                    Regenerate API key
                                </UButton>
                            </div>
                            <div class="mt-4">
                                <p class="text-xs uppercase tracking-wide text-muted mb-1.5">Synced indexers</p>
                                <p v-if="!syncedIndexers.length" class="text-dimmed text-sm">None synced from Prowlarr yet.</p>
                                <ul v-else class="flex flex-col gap-1 text-sm">
                                    <li v-for="idx in syncedIndexers" :key="`${idx.name}-${idx.protocol}`" class="flex items-center gap-2">
                                        <span>{{ idx.name }}</span>
                                        <span class="text-dimmed text-xs">{{ idx.protocol }}</span>
                                        <UBadge :color="idx.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">
                                            {{ idx.enabled ? 'enabled' : 'disabled' }}
                                        </UBadge>
                                    </li>
                                </ul>
                            </div>
                        </UCard>

                        <UCard>
                            <template #header>
                                <SettingsHeader
                                    title="Download clients"
                                    subtitle="Where releases are sent to download (qBittorrent, Transmission…).">
                                    <UBadge :color="downloadClients.length ? 'success' : 'neutral'" variant="subtle">
                                        {{ downloadClients.length }}
                                    </UBadge>
                                </SettingsHeader>
                            </template>
                            <p v-if="!downloadClients.length" class="text-dimmed text-sm">No download clients configured.</p>
                            <ul v-else class="flex flex-col gap-1.5 text-sm">
                                <li v-for="client in downloadClients" :key="client.id" class="flex items-center gap-2">
                                    <span>{{ client.name }}</span>
                                    <span class="text-dimmed text-xs">{{ client.type }} · {{ client.baseUrl }}</span>
                                    <UBadge :color="client.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">
                                        {{ client.enabled ? 'enabled' : 'disabled' }}
                                    </UBadge>
                                    <UButton size="xs" variant="ghost" icon="i-lucide-pencil" @click="openDownloadClient(client)" />
                                    <UButton
                                        size="xs"
                                        variant="ghost"
                                        color="error"
                                        icon="i-lucide-trash"
                                        @click="removeDownloadClient(client.id)" />
                                </li>
                            </ul>
                            <template #footer>
                                <UButton icon="i-lucide-plus" color="primary" class="w-fit" @click="openDownloadClient(null)">
                                    Add download client
                                </UButton>
                            </template>
                        </UCard>

                        <UCard>
                            <template #header>
                                <SettingsHeader title="Comic metadata (Metron)" subtitle="Optional source for richer comic metadata.">
                                    <UBadge :color="metronConnected ? 'success' : 'neutral'" variant="subtle">
                                        {{ metronConnected ? 'Connected' : 'Not connected' }}
                                    </UBadge>
                                </SettingsHeader>
                            </template>
                            <UButton
                                :icon="metronConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                :color="metronConnected ? 'neutral' : 'primary'"
                                variant="soft"
                                class="w-fit"
                                @click="onMetronClick">
                                {{ metronConnected ? 'Disconnect Metron' : 'Connect Metron' }}
                            </UButton>
                        </UCard>
                    </div>
                </template>

                <!-- NOTIFICATIONS -->
                <template #notifications>
                    <UCard class="max-w-3xl">
                        <template #header>
                            <SettingsHeader title="Notifications" subtitle="Get pinged when Kenku downloads new chapters." />
                        </template>
                        <NotificationConnectors />
                        <template #footer>
                            <div class="flex flex-row flex-wrap gap-2">
                                <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="addGotifyModal.open()">Gotify</UButton>
                                <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="addNtfyModal.open()">Ntfy</UButton>
                                <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="addPushoverModal.open()"
                                    >Pushover</UButton
                                >
                                <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="addGenericConnectorModal.open()">
                                    Generic webhook
                                </UButton>
                            </div>
                        </template>
                    </UCard>
                </template>

                <!-- MAINTENANCE -->
                <template #maintenance>
                    <UCard class="max-w-3xl">
                        <template #header>
                            <SettingsHeader title="Maintenance" subtitle="Housekeeping for the database and the action log." />
                        </template>
                        <div class="flex gap-2 flex-wrap">
                            <UButton icon="i-lucide-database" variant="soft" loading-auto class="w-fit" @click="cleanUpDatabase">
                                Clean database
                            </UButton>
                            <UButton icon="i-lucide-captions-off" variant="soft" loading-auto class="w-fit" @click="cleanUpActions">
                                Clean actions
                            </UButton>
                        </div>
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
import {
    LazyAddLibraryModal,
    LazyGenericNotificationConnectorModal,
    LazyGotifyModal,
    LazyKavitaModal,
    LazyKomgaModal,
    LazyNtfyModal,
    LazyPushoverModal,
    LazyMetronModal,
    LazyDownloadClientModal,
} from '#components';
import FileLibraries from '~/components/FileLibraries.vue';
import { refreshNuxtData } from '#app';
import type { components } from '#open-fetch-schemas/api';
type DownloadClientResponse = components['schemas']['DownloadClientResponse'];
const overlay = useOverlay();
const { $api } = useNuxtApp();

const addLibraryModal = overlay.create(LazyAddLibraryModal);
const komgaModal = overlay.create(LazyKomgaModal);
const kavitaModal = overlay.create(LazyKavitaModal);

const addGotifyModal = overlay.create(LazyGotifyModal);
const addNtfyModal = overlay.create(LazyNtfyModal);
const addPushoverModal = overlay.create(LazyPushoverModal);
const addGenericConnectorModal = overlay.create(LazyGenericNotificationConnectorModal);
const downloadClientModal = overlay.create(LazyDownloadClientModal);
const metronModal = overlay.create(LazyMetronModal);

const cleanUpDatabase = async () => {
    await useApi('/v2/Maintenance/CleanupNoDownloadManga', { method: 'POST' });
    await refreshNuxtData(FetchKeys.Series.All);
};
const cleanUpActions = async () => {
    await useApi('/v2/Maintenance/CleanupActions', { method: 'POST' });
};

const { data: libraries } = useApi('/v2/LibraryConnector', { key: FetchKeys.Libraries.All, server: false });
const komgaConnected = computed(() => libraries.value?.find((l) => l.type === 'Komga'));
const onKomgaClick = async () => {
    if (!komgaConnected.value) {
        komgaModal.open();
    } else {
        await $api('/v2/LibraryConnector/{LibraryConnectorId}', {
            method: 'DELETE',
            path: { LibraryConnectorId: komgaConnected.value.key },
        });
        await refreshNuxtData(FetchKeys.Libraries.All);
    }
};
const kavitaConnected = computed(() => libraries.value?.find((l) => l.type === 'Kavita'));
const onKavitaClick = async () => {
    if (!kavitaConnected.value) {
        kavitaModal.open();
    } else {
        await $api('/v2/LibraryConnector/{LibraryConnectorId}', {
            method: 'DELETE',
            path: { LibraryConnectorId: kavitaConnected.value.key },
        });
        await refreshNuxtData(FetchKeys.Libraries.All);
    }
};

const { data: settingsData, status: settingsStatus } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });

const metronConnected = computed(() => !!settingsData.value?.metronConfigured);
const apiKey = computed(() => settingsData.value?.apiKey ?? '');
const syncedIndexers = computed(() => settingsData.value?.syncedIndexers ?? []);
const downloadClients = computed(() => settingsData.value?.downloadClients ?? []);
const baseUrl = computed(() => (import.meta.client ? window.location.origin : ''));

const copy = async (value: string) => {
    try {
        await navigator.clipboard.writeText(value);
    } catch {
        /* clipboard unavailable */
    }
};

const regenerateApiKey = async () => {
    await $api('/v2/Settings/ApiKey/Regenerate', { method: 'POST' });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const openDownloadClient = (client?: DownloadClientResponse | null) => {
    downloadClientModal.open({ client });
};
const removeDownloadClient = async (id?: number) => {
    if (id === undefined) return;
    await $api('/v2/Settings/DownloadClients/{id}', { method: 'DELETE', path: { id } });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const onMetronClick = async () => {
    if (!metronConnected.value) {
        metronModal.open();
        return;
    }
    await $api('/v2/Settings/Metron', { method: 'DELETE' });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const { data: stats } = useApi('/v2/Stats', { server: false });
const deCamel = (camel: string): string =>
    camel.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/(^\w{1})|(\s+\w{1})/g, (letter) => letter.toUpperCase());

const tabs = [
    { label: 'Library', icon: 'i-lucide-folder-tree', slot: 'library' as const },
    { label: 'Downloading', icon: 'i-lucide-download', slot: 'downloading' as const },
    { label: 'Notifications', icon: 'i-lucide-bell', slot: 'notifications' as const },
    { label: 'Maintenance', icon: 'i-lucide-wrench', slot: 'maintenance' as const },
];

useHead({ title: 'Settings' });
</script>
