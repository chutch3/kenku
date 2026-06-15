<template>
    <UCard>
        <template #header>
            <SettingsHeader title="Download clients" subtitle="Where releases are sent to download (qBittorrent, Transmission…).">
                <UBadge :color="downloadClients.length ? 'success' : 'neutral'" variant="subtle">{{ downloadClients.length }}</UBadge>
            </SettingsHeader>
        </template>
        <p v-if="!downloadClients.length" class="text-dimmed text-sm">No download clients configured.</p>
        <ul v-else class="flex flex-col gap-1.5 text-sm">
            <li v-for="client in downloadClients" :key="client.id" class="flex items-center gap-2">
                <span>{{ client.name }}</span>
                <span class="text-dimmed text-xs">{{ client.type }} · {{ client.baseUrl }}</span>
                <UBadge :color="client.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">{{ client.enabled ? 'enabled' : 'disabled' }}</UBadge>
                <UButton size="xs" variant="ghost" icon="i-lucide-pencil" aria-label="Edit download client" @click="openClient(client)" />
                <UButton size="xs" variant="ghost" color="error" icon="i-lucide-trash" aria-label="Remove download client" @click="removeDownloadClient(client.id)" />
            </li>
        </ul>
        <template #footer>
            <UButton icon="i-lucide-plus" color="primary" class="w-fit" @click="openClient(null)">Add download client</UButton>
        </template>
    </UCard>
</template>

<script setup lang="ts">
import { LazyDownloadClientModal } from '#components';
import type { components } from '#open-fetch-schemas/api';
type DownloadClientResponse = components['schemas']['DownloadClientResponse'];

const { downloadClients, removeDownloadClient } = useSettings();
const overlay = useOverlay();
const downloadClientModal = overlay.create(LazyDownloadClientModal);
const openClient = (client?: DownloadClientResponse | null) => downloadClientModal.open({ client });
</script>
