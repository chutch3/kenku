<template>
    <section v-if="marked.length" class="flex flex-col gap-2">
        <div class="flex items-center gap-2">
            <h2 class="font-display text-lg font-semibold text-highlighted">{{ title }}</h2>
            <span class="h-px w-8 bg-vermillion-500 shadow-[0_0_8px_var(--color-vermillion-500)]" />
            <span v-if="subtitle" class="font-mono text-[0.6rem] uppercase tracking-[0.2em] text-muted">{{ subtitle }}</span>
        </div>
        <div class="flex gap-3 overflow-x-auto pb-2">
            <component
                :is="external ? 'a' : 'button'"
                v-for="m in marked"
                :key="m.entry.url || m.entry.title"
                :href="external ? m.entry.url : undefined"
                :target="external ? '_blank' : undefined"
                rel="noopener"
                class="kenku-lift relative w-28 shrink-0 text-left cursor-pointer group"
                :title="m.entry.blurb ?? m.entry.title ?? undefined"
                @click="onClick(m)">
                <div
                    class="relative h-40 w-28 rounded-lg overflow-clip ring-1"
                    :class="m.inLibrary ? 'ring-jade-500/70' : 'ring-default'">
                    <FallbackImage
                        :src="m.entry.coverUrl"
                        :alt="m.entry.title ?? ''"
                        class="w-full h-full object-cover transition-transform duration-500 group-hover:scale-105" />
                    <div class="absolute inset-x-0 bottom-0 pt-8 pb-1.5 px-1.5 bg-gradient-to-t from-black/90 via-black/55 to-transparent">
                        <p class="text-[0.7rem]/tight font-medium text-white line-clamp-3 [text-shadow:0_1px_8px_rgba(0,0,0,0.6)]">
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
                    <div
                        v-if="resolving && (m.entry.url || m.entry.title) === resolving"
                        class="absolute inset-0 grid place-items-center bg-black/50">
                        <UIcon name="i-lucide-loader-circle" class="size-6 animate-spin text-white" />
                    </div>
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
    /** Entry key (url, or title) currently resolving into the add flow — shows a spinner, blocks clicks. */
    resolving?: string | null;
}>();
const emit = defineEmits<{ (e: 'pick', entry: Entry): void; (e: 'open', seriesKey: string): void }>();

const marked = computed(() =>
    (props.entries ?? []).map((entry) => ({
        entry,
        inLibrary: props.external
            ? undefined
            : (props.library ?? []).find((s) => s.fileLibraryId && normalizeTitle(s.name) === normalizeTitle(entry.title)),
    }))
);

const onClick = (m: { entry: Entry; inLibrary?: MinimalSeries }) => {
    if (props.external || props.resolving) return;
    if (m.inLibrary) emit('open', m.inLibrary.key);
    else emit('pick', m.entry);
};
</script>
