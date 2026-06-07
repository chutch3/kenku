<template>
    <!-- Atmosphere: ink/paper base, manga screentone, grain, ambient glows. -->
    <div class="kenku-atmosphere" aria-hidden="true">
        <div class="kenku-glow kenku-glow--vermillion" />
        <div class="kenku-glow kenku-glow--jade" />
    </div>

    <UApp>
        <UHeader :toggle="true" class="backdrop-blur-md bg-default/70">
            <template #left>
                <NuxtLink to="/" class="group">
                    <div class="flex gap-2.5 items-center">
                        <KenkuMark :size="34" class="seal-in transition-transform duration-300 group-hover:rotate-[8deg]" />
                        <div class="flex flex-col leading-none">
                            <span class="font-display text-3xl font-semibold tracking-tight text-highlighted">Kenku</span>
                            <span class="font-mono text-[0.6rem] uppercase tracking-[0.32em] text-vermillion-500/90 -mt-0.5">
                                烏 · karasu
                            </span>
                        </div>
                    </div>
                </NuxtLink>
            </template>

            <template #default>
                <UNavigationMenu :items="primaryNav" variant="link" />
            </template>

            <template #body>
                <UNavigationMenu :items="primaryNav" orientation="vertical" variant="link" class="-mx-2.5" />
                <USeparator class="my-4" />
                <UNavigationMenu :items="devLinks" orientation="vertical" variant="link" class="-mx-2.5" />
            </template>

            <template #right>
                <UButton color="neutral" variant="ghost" class="max-sm:hidden gap-1.5" aria-label="Search" @click="cmdkOpen = true">
                    <UIcon name="i-lucide-search" class="size-4" />
                    <span class="text-dimmed text-sm">Search</span>
                    <span class="flex gap-0.5">
                        <UKbd value="meta" />
                        <UKbd value="k" />
                    </span>
                </UButton>
                <UButton
                    icon="i-lucide-search"
                    color="neutral"
                    variant="ghost"
                    class="sm:hidden"
                    aria-label="Search"
                    @click="cmdkOpen = true" />

                <UButton icon="i-lucide-plus" to="/search" color="primary">
                    <span class="max-sm:hidden">Add series</span>
                </UButton>

                <UColorModeButton />

                <UDropdownMenu :items="devLinks as DropdownMenuItem[]" :content="{ align: 'end' }">
                    <UButton icon="i-lucide-ellipsis-vertical" color="neutral" variant="ghost" aria-label="More" class="max-sm:hidden" />
                </UDropdownMenu>
            </template>
        </UHeader>

        <UMain>
            <UPage>
                <NuxtPage />
            </UPage>
        </UMain>

        <AppCommandPalette />
    </UApp>
</template>

<script setup lang="ts">
import type { NavigationMenuItem, DropdownMenuItem } from '@nuxt/ui';

const cmdkOpen = useState('cmdk-open', () => false);

const primaryNav = computed<NavigationMenuItem[]>(() => [
    { label: 'Library', to: '/', icon: 'i-lucide-library', exact: true },
    { label: 'Activity', to: '/actions', icon: 'i-lucide-scroll-text' },
    { label: 'Queue', to: '/queue', icon: 'i-lucide-layers' },
    { label: 'Settings', to: '/settings', icon: 'i-lucide-settings' },
]);

const devLinks = computed<NavigationMenuItem[]>(() => [
    { label: 'GitHub', to: 'https://github.com/chutch3/kenku', icon: 'i-lucide-github', target: '_blank' },
    { label: 'API docs', to: `${useRuntimeConfig().public.openFetch.api.baseURL}swagger`, icon: 'i-lucide-book-open', target: '_blank' },
]);
</script>
