<template>
    <UFormField label="Rails" description="Which rails show on the Discover page. New rails appear on automatically.">
        <div class="flex flex-col gap-2">
            <div v-for="rail in catalog ?? []" :key="rail.id ?? ''" class="flex items-center gap-2">
                <USwitch
                    :model-value="!disabled.includes(rail.id ?? '')"
                    :aria-label="`Show the ${rail.label} rail`"
                    @update:model-value="(on) => toggle(rail.id ?? '', on)" />
                <span class="text-sm text-highlighted">{{ rail.label }}</span>
                <UBadge size="sm" variant="subtle" color="neutral">{{ rail.contentType === 'Comic' ? 'comic' : 'manga' }}</UBadge>
            </div>
        </div>
    </UFormField>
</template>

<script setup lang="ts">
const { $api } = useNuxtApp();
const toast = useToast();

const { data: settings } = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
const { data: catalog } = useApi('/v2/Discover/RailCatalog', { key: FetchKeys.Discover.RailCatalog, server: false });

// DiscoveryRails is a denylist of disabled rail ids; a rail is on unless its id is in it.
const disabled = computed(() => settings.value?.discoveryRails ?? []);

const toggle = async (id: string, on: boolean) => {
    const next = on ? disabled.value.filter((d) => d !== id) : [...disabled.value, id];
    try {
        await $api('/v2/Settings/DiscoveryRails', { method: 'PATCH', body: next });
        await refreshNuxtData(FetchKeys.Settings.All);
    } catch {
        toast.add({ title: "Couldn't update rails", icon: 'i-lucide-triangle-alert', color: 'error' });
    }
};
</script>
