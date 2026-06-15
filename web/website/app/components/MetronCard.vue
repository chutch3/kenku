<template>
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
            @click="onClick">
            {{ metronConnected ? 'Disconnect Metron' : 'Connect Metron' }}
        </UButton>
    </UCard>
</template>

<script setup lang="ts">
import { LazyMetronModal } from '#components';

const { metronConnected, disconnectMetron } = useSettings();
const overlay = useOverlay();
const metronModal = overlay.create(LazyMetronModal);
const onClick = () => (metronConnected.value ? disconnectMetron() : metronModal.open());
</script>
