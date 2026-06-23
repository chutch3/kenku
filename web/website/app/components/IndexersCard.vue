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

        <div class="mt-5 pt-4 border-t border-default">
            <p class="text-xs uppercase tracking-wide text-muted mb-1.5">Manual indexers</p>
            <p class="text-xs text-muted mb-2">Add a Torznab/Newznab feed directly, without Prowlarr. Takes effect immediately.</p>
            <ul v-if="manualIndexers.length" class="flex flex-col gap-1 text-sm mb-3">
                <li v-for="idx in manualIndexers" :key="idx.name ?? ''" class="flex items-center gap-2">
                    <span class="text-highlighted">{{ idx.name }}</span>
                    <span class="text-dimmed text-xs truncate">{{ idx.url }}</span>
                    <IndexerCooldownBadge :cooldown-until="idx.cooldownUntil" />
                    <UButton
                        class="ms-auto shrink-0" color="error" variant="ghost" size="xs" icon="i-lucide-trash"
                        :aria-label="`Remove ${idx.name}`" loading-auto @click="removeManualIndexer(idx.name ?? '')" />
                </li>
            </ul>
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <UInput v-model="form.name" placeholder="Indexer name" />
                <UInput v-model="form.url" placeholder="Torznab/Newznab URL" />
                <UInput v-model="form.apiKey" type="password" placeholder="API key" />
                <UInput v-model="form.categories" placeholder="Categories (e.g. 7030, 7000)" />
            </div>
            <UButton
                class="mt-2 w-fit" icon="i-lucide-plus" variant="soft" :disabled="!form.name.trim() || !form.url.trim()"
                aria-label="Add manual indexer" loading-auto @click="addIndexer">Add indexer</UButton>
        </div>
    </UCard>
</template>

<script setup lang="ts">
const { apiKey, syncedIndexers, manualIndexers, regenerateApiKey, copy, addManualIndexer, removeManualIndexer } = useSettings();
const baseUrl = computed(() => (import.meta.client ? window.location.origin : ''));

const form = reactive({ name: '', url: '', apiKey: '', categories: '' });
const addIndexer = async () => {
    const categories = form.categories
        .split(',')
        .map((c) => Number.parseInt(c.trim(), 10))
        .filter((n) => Number.isFinite(n));
    await addManualIndexer({ name: form.name.trim(), url: form.url.trim(), apiKey: form.apiKey, categories });
    form.name = form.url = form.apiKey = form.categories = '';
};
</script>
