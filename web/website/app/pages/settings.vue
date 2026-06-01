<template>
    <KenkuPage>
        <UPageSection title="Settings" :ui="{ container: 'py-2 sm:py-2 lg:py-2 gap-2' }">
            <template #description>
                <div v-if="settingsStatus === 'error'">
                    <p class="text-warning">Unable to connect to api.</p>
                    <p class="">NUXT_PUBLIC_OPEN_FETCH_API_BASE_URL: {{ $config.public.openFetch.api.baseURL }}</p>
                </div>
            </template>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Libraries</h1>
                </template>
                <template #footer>
                    <div class="flex flex-row gap-2">
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addLibraryModal.open()">Add FileLibrary</UButton>
                        <UTooltip :text="komgaConnected ? 'Disconnect Komga' : 'Connect Komga'">
                            <UButton
                                :icon="komgaConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                class="w-fit"
                                label="Komga"
                                @click="onKomgaClick" />
                        </UTooltip>
                        <UTooltip :text="kavitaConnected ? 'Disconnect Kavita' : 'Connect Kavita'">
                            <UButton
                                :icon="kavitaConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                class="w-fit"
                                label="Kavita"
                                @click="onKavitaClick" />
                        </UTooltip>
                    </div>
                </template>
                <FileLibraries />
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Notifications</h1>
                </template>
                <NotificationConnectors />
                <template #footer>
                    <div class="flex flex-row gap-2">
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addGotifyModal.open()">Add Gotify</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addNtfyModal.open()">Add Ntfy</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addPushoverModal.open()">Add Pushover</UButton>
                        <UButton icon="i-lucide-plus" class="w-fit" @click="addGenericConnectorModal.open()"
                            >Add Generic Notification Connector</UButton
                        >
                    </div>
                </template>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Comics &amp; Torrents</h1>
                </template>
                <p class="text-dimmed text-sm mb-2">
                    Add Kenku to Prowlarr as a <b>Mylar</b> application (Settings &rarr; Apps). Use the URL and API key
                    below; Prowlarr will sync your comic indexers into Kenku automatically. Configure one or more
                    download clients to fetch releases. Metron provides comic metadata.
                </p>
                <UFormField label="Kenku Base URL (Mylar Server)">
                    <UInput :model-value="baseUrl" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                        <template #trailing>
                            <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" @click="copy(baseUrl)" />
                        </template>
                    </UInput>
                </UFormField>
                <UFormField label="API Key" class="mt-2">
                    <UInput :model-value="apiKey" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                        <template #trailing>
                            <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" @click="copy(apiKey)" />
                        </template>
                    </UInput>
                </UFormField>
                <div class="mt-2">
                    <UButton icon="i-lucide-refresh-cw" class="w-fit" loading-auto @click="regenerateApiKey">Regenerate API Key</UButton>
                </div>

                <h2 class="mt-4 font-semibold">Synced Indexers</h2>
                <p v-if="!syncedIndexers.length" class="text-dimmed text-sm">No indexers synced from Prowlarr yet.</p>
                <ul v-else class="text-sm">
                    <li v-for="idx in syncedIndexers" :key="`${idx.name}-${idx.protocol}`">
                        {{ idx.name }} <span class="text-dimmed">({{ idx.protocol }})</span>
                        <UBadge :color="idx.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">
                            {{ idx.enabled ? 'enabled' : 'disabled' }}
                        </UBadge>
                    </li>
                </ul>

                <h2 class="mt-4 font-semibold">Download Clients</h2>
                <p v-if="!downloadClients.length" class="text-dimmed text-sm">No download clients configured.</p>
                <ul v-else class="text-sm">
                    <li v-for="client in downloadClients" :key="client.id" class="flex items-center gap-2">
                        <span>{{ client.name }} <span class="text-dimmed">({{ client.type }} &middot; {{ client.baseUrl }})</span></span>
                        <UBadge :color="client.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">
                            {{ client.enabled ? 'enabled' : 'disabled' }}
                        </UBadge>
                        <UButton size="xs" variant="ghost" icon="i-lucide-pencil" @click="openDownloadClient(client)" />
                        <UButton size="xs" variant="ghost" icon="i-lucide-trash" @click="removeDownloadClient(client.id)" />
                    </li>
                </ul>
                <template #footer>
                    <div class="flex flex-row gap-2 flex-wrap">
                        <UButton icon="i-lucide-plus" class="w-fit" @click="openDownloadClient(null)">Add Download Client</UButton>
                        <UTooltip :text="metronConnected ? 'Disconnect Metron' : 'Connect Metron'">
                            <UButton
                                :icon="metronConnected ? 'i-lucide-unlink' : 'i-lucide-link'"
                                class="w-fit"
                                label="Metron"
                                @click="onMetronClick" />
                        </UTooltip>
                    </div>
                </template>
            </UCard>
            <UCard v-if="settingsStatus === 'success'">
                <template #header>
                    <h1>Maintenance</h1>
                </template>
                <div class="flex gap-2">
                    <UButton icon="i-lucide-database" loading-auto class="w-fit mb-2" @click="cleanUpDatabase">Clean database</UButton>
                    <UButton icon="i-lucide-captions-off" loading-auto class="w-fit mb-2" @click="cleanUpActions">Clean actions</UButton>
                </div>
            </UCard>
            <UCard>
                <template #header>
                    <h1>Stats</h1>
                </template>
                <div class="flex flex-row flex-wrap gap-2">
                    <UBadge v-for="(value, name) in stats" :key="name" variant="outline" color="neutral">
                        {{ deCamel(name) }}: {{ value }}
                    </UBadge>
                </div>
            </UCard>
        </UPageSection>
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

const { data: libraries } = useApi('/v2/LibraryConnector', { key: FetchKeys.Libraries.All });
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

const metronConnected = computed(() => !!settingsData.value?.metronUsername);
const apiKey = computed(() => settingsData.value?.apiKey ?? '');
const syncedIndexers = computed(() => settingsData.value?.syncedIndexers ?? []);
const downloadClients = computed(() => settingsData.value?.downloadClients ?? []);
const baseUrl = computed(() => (import.meta.client ? window.location.origin : ''));

const copy = async (value: string) => {
    try { await navigator.clipboard.writeText(value); } catch { /* clipboard unavailable */ }
};

const regenerateApiKey = async () => {
    await $api('/v2/Settings/ApiKey/Regenerate', { method: 'POST' });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const openDownloadClient = (client: unknown) => {
    downloadClientModal.open({ client });
};
const removeDownloadClient = async (id: number) => {
    await $api('/v2/Settings/DownloadClients/{id}', { method: 'DELETE', path: { id } });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const onMetronClick = async () => {
    if (!metronConnected.value) { metronModal.open(); return; }
    await $api('/v2/Settings/Metron', { method: 'DELETE' });
    await refreshNuxtData(FetchKeys.Settings.All);
};

const { data: stats } = useApi('/v2/Stats', { server: false });
const deCamel = (camel: string): string =>
    camel.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/(^\w{1})|(\s+\w{1})/g, (letter) => letter.toUpperCase());

useHead({ title: 'Settings' });
</script>
