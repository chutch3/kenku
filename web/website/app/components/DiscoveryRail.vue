<template>
    <section v-if="marked.length" class="flex flex-col gap-2">
        <div class="flex items-baseline gap-2">
            <h3 class="font-display text-base font-semibold text-highlighted">{{ title }}</h3>
            <span v-if="subtitle" class="text-xs text-muted">{{ subtitle }}</span>
        </div>
        <div class="flex gap-3 overflow-x-auto pb-2">
            <component
                :is="external ? 'a' : 'button'"
                v-for="m in marked"
                :key="m.entry.url || m.entry.title"
                :href="external ? m.entry.url : undefined"
                :target="external ? '_blank' : undefined"
                rel="noopener"
                class="kenku-lift relative max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) shrink-0 text-left cursor-pointer group"
                :title="m.entry.blurb ?? m.entry.title ?? undefined"
                @click="onClick(m)">
                <div
                    class="relative max-sm:h-[var(--mangacover-height-sm)] h-(--mangacover-height) max-sm:w-[var(--mangacover-width-sm)] w-(--mangacover-width) rounded-lg overflow-clip ring-1"
                    :class="m.inLibrary ? 'ring-jade-500/70' : 'ring-default'">
                    <FallbackImage
                        :src="m.entry.coverUrl"
                        :alt="m.entry.title ?? ''"
                        class="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105" />
                    <div class="absolute inset-x-0 bottom-0 pt-10 pb-2 px-2 bg-gradient-to-t from-black/90 via-black/55 to-transparent">
                        <p class="text-sm/tight font-semibold text-white line-clamp-3 [text-shadow:0_1px_8px_rgba(0,0,0,0.6)]">
                            {{ m.entry.title }}
                        </p>
                    </div>
                    <UBadge
                        v-if="m.inLibrary"
                        color="success"
                        variant="solid"
                        size="sm"
                        icon="i-lucide-check"
                        class="absolute top-1 right-1">In library</UBadge>
                    <UIcon
                        v-else-if="external"
                        name="i-lucide-external-link"
                        class="absolute top-1.5 right-1.5 size-4 text-white/90 [filter:drop-shadow(0_1px_4px_rgba(0,0,0,0.8))]" />
                    <!-- Persistent add affordance: discoverable on touch, where hover never fires. -->
                    <UBadge
                        v-else
                        color="primary"
                        variant="solid"
                        size="sm"
                        icon="i-lucide-plus"
                        class="absolute top-1 right-1 opacity-95">Add</UBadge>
                </div>
            </component>
        </div>
    </section>
</template>

<script setup lang="ts">
import type { components } from '#open-fetch-schemas/api';
type Entry = components['schemas']['DiscoveryEntry'];
type MinimalSeries = components['schemas']['MinimalSeries'];

const props = defineProps<{
    title: string;
    subtitle?: string;
    entries?: Entry[] | null;
    /** Tracked series to match against — matching entries are highlighted and open in place. */
    library?: MinimalSeries[] | null;
    /** Feed rails link out (reddit posts aren't series); series rails emit into the add flow. */
    external?: boolean;
    /** Normalized titles already shown by an earlier rail — dropped here so rails don't repeat. */
    exclude?: string[];
}>();
const emit = defineEmits<{ (e: 'pick', entry: Entry): void; (e: 'open', seriesKey: string): void }>();

const marked = computed(() => {
    const excluded = new Set(props.exclude ?? []);
    return (props.entries ?? [])
        .filter((entry) => !excluded.has(normalizeTitle(entry.title)))
        .map((entry) => ({
            entry,
            inLibrary: props.external
                ? undefined
                : (props.library ?? []).find((s) => s.fileLibraryId && normalizeTitle(s.name) === normalizeTitle(entry.title)),
        }))
        // Lead with what you don't have; owned titles sink to the tail. Library-sized covers make a
        // long rail a wall, so twelve is a browse.
        .sort((a, b) => Number(!!a.inLibrary) - Number(!!b.inLibrary))
        .slice(0, 12);
});

const onClick = (m: { entry: Entry; inLibrary?: MinimalSeries }) => {
    if (props.external) return;
    if (m.inLibrary) emit('open', m.inLibrary.key);
    else emit('pick', m.entry);
};
</script>
