<template>
    <UCard>
        <template #header>
            <SettingsHeader
                title="Indexers"
                subtitle="Where Kenku searches for releases — synced from Prowlarr, or added by hand.">
                <UBadge :color="syncedIndexers.length || manualIndexers.length ? 'success' : 'neutral'" variant="subtle">
                    {{ syncedIndexers.length + manualIndexers.length }}
                </UBadge>
            </SettingsHeader>
        </template>
        <div class="rounded-lg bg-elevated ring-1 ring-default px-3 py-2 mb-3">
            <p class="text-xs text-muted">
                In Prowlarr: <b class="text-toned">Settings → Apps → Add → Mylar</b>, then paste the URL and API key below.
            </p>
        </div>
        <UFormField label="Kenku base URL (Mylar server)">
            <UInput :model-value="baseUrl" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                <template #trailing>
                    <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" aria-label="Copy base URL" @click="copy(baseUrl)" />
                </template>
            </UInput>
        </UFormField>
        <UFormField label="API key" class="mt-2">
            <UInput :model-value="apiKey" readonly class="w-full" :ui="{ trailing: 'pe-1' }">
                <template #trailing>
                    <UButton color="neutral" variant="link" size="sm" icon="i-lucide-copy" aria-label="Copy API key" @click="copy(apiKey)" />
                </template>
            </UInput>
        </UFormField>
        <div class="mt-3">
            <UButton icon="i-lucide-refresh-cw" variant="soft" class="w-fit" loading-auto @click="regenerateApiKey">Regenerate API key</UButton>
        </div>
        <div class="mt-4">
            <p class="text-xs uppercase tracking-wide text-muted mb-1.5">Synced indexers</p>
            <p v-if="!syncedIndexers.length" class="text-dimmed text-sm">None synced from Prowlarr yet.</p>
            <ul v-else class="flex flex-col gap-1 text-sm">
                <li v-for="idx in syncedIndexers" :key="`${idx.name}-${idx.protocol}`" class="flex items-center gap-2">
                    <span>{{ idx.name }}</span>
                    <span class="text-dimmed text-xs">{{ idx.protocol }}</span>
                    <UBadge :color="idx.enabled ? 'success' : 'neutral'" variant="subtle" size="sm">{{ idx.enabled ? 'enabled' : 'disabled' }}</UBadge>
                    <IndexerCooldownBadge :cooldown-until="idx.cooldownUntil" />
                </li>
            </ul>
        </div>

        <div class="mt-5 pt-4 border-t border-default">
            <div class="flex items-center justify-between mb-1.5">
                <p class="text-xs uppercase tracking-wide text-muted">Manual indexers</p>
                <UButton size="xs" variant="soft" icon="i-lucide-plus" aria-label="Add manual indexer" @click="openIndexer(null)">Add</UButton>
            </div>
            <p class="text-xs text-muted mb-2">Torznab/Newznab feeds added directly, without Prowlarr. Take effect immediately.</p>
            <p v-if="!manualIndexers.length" class="text-dimmed text-sm">None added.</p>
            <ul v-else class="flex flex-col gap-1 text-sm">
                <li v-for="idx in manualIndexers" :key="idx.name ?? ''" class="flex items-center gap-2">
                    <span class="text-highlighted">{{ idx.name }}</span>
                    <span class="text-dimmed text-xs truncate">{{ idx.url }}</span>
                    <IndexerCooldownBadge :cooldown-until="idx.cooldownUntil" />
                    <UButton
                        class="ms-auto shrink-0" size="xs" variant="ghost" icon="i-lucide-pencil"
                        :aria-label="`Edit ${idx.name}`" @click="openIndexer(idx)" />
                    <UButton
                        class="shrink-0" size="xs" variant="ghost" color="error" icon="i-lucide-trash"
                        :aria-label="`Remove ${idx.name}`" loading-auto @click="removeManualIndexer(idx.name ?? '')" />
                </li>
            </ul>
        </div>
    </UCard>
</template>

<script setup lang="ts">
import { LazyManualIndexerModal } from '#components';
import type { components } from '#open-fetch-schemas/api';
type ManualIndexerResponse = components['schemas']['ManualIndexerResponse'];

const { apiKey, syncedIndexers, manualIndexers, regenerateApiKey, copy, removeManualIndexer } = useSettings();
const baseUrl = computed(() => (import.meta.client ? window.location.origin : ''));

const overlay = useOverlay();
const indexerModal = overlay.create(LazyManualIndexerModal);
const openIndexer = (indexer?: ManualIndexerResponse | null) => indexerModal.open({ indexer });
</script>
