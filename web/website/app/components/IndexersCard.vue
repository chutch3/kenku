<template>
    <UCard>
        <template #header>
            <SettingsHeader
                title="Indexers via Prowlarr"
                subtitle="Kenku appears as a Mylar app in Prowlarr, which syncs your comic indexers automatically.">
                <UBadge :color="syncedIndexers.length ? 'success' : 'neutral'" variant="subtle">{{ syncedIndexers.length }} synced</UBadge>
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
    </UCard>
</template>

<script setup lang="ts">
const { apiKey, syncedIndexers, regenerateApiKey, copy } = useSettings();
const baseUrl = computed(() => (import.meta.client ? window.location.origin : ''));
</script>
