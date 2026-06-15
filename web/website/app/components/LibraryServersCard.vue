<template>
    <UCard>
        <template #header>
            <SettingsHeader
                title="Library servers"
                subtitle="Connect a reader like Komga or Kavita: when downloads land, Kenku tells it to rescan its libraries so new files appear. Point the reader at the same folders Kenku writes to — no metadata is synced." />
        </template>
        <div class="flex flex-col gap-2">
            <div v-for="srv in servers" :key="srv.name" class="flex items-center gap-3 bg-elevated rounded-lg px-3 py-2">
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
</template>

<script setup lang="ts">
import { LazyKomgaModal, LazyKavitaModal } from '#components';

const { komgaConnected, kavitaConnected, disconnectLibrary } = useSettings();
const overlay = useOverlay();
const komgaModal = overlay.create(LazyKomgaModal);
const kavitaModal = overlay.create(LazyKavitaModal);

const servers = computed(() => [
    { name: 'Komga', connected: !!komgaConnected.value, onClick: () => (komgaConnected.value ? disconnectLibrary(komgaConnected.value.key) : komgaModal.open()) },
    { name: 'Kavita', connected: !!kavitaConnected.value, onClick: () => (kavitaConnected.value ? disconnectLibrary(kavitaConnected.value.key) : kavitaModal.open()) },
]);
</script>
