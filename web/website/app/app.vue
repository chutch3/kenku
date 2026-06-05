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
            <template #body>
                <UNavigationMenu :items="items" orientation="vertical" variant="link" color="neutral" />
            </template>
            <template #default>
                <UNavigationMenu :items="items" orientation="horizontal" variant="link" color="neutral" />
            </template>
            <template #right>
                <UTooltip text="Activity log">
                    <UButton
                        icon="i-lucide-scroll-text"
                        :to="`/actions?return=${$route.fullPath}`"
                        :disabled="$route.fullPath.startsWith('/actions')"
                        variant="ghost"
                        color="neutral" />
                </UTooltip>
                <UButton icon="i-lucide-plus" to="/search" color="primary" variant="solid">
                    <template #default>
                        <span class="max-sm:hidden">Add series</span>
                    </template>
                </UButton>
                <UColorModeButton />
                <UTooltip text="Settings">
                    <UButton icon="i-lucide-settings" variant="ghost" to="/settings" color="neutral" />
                </UTooltip>
            </template>
        </UHeader>
        <UMain>
            <UPage>
                <NuxtPage />
            </UPage>
        </UMain>
    </UApp>
</template>

<script setup lang="ts">
import type { NavigationMenuItem } from '#ui/components/NavigationMenu.vue';

const items = computed<NavigationMenuItem[]>(() => [
    { label: 'Library', to: '/', icon: 'i-lucide-library' },
    { label: 'GitHub', to: 'https://github.com/chutch3/kenku', icon: 'i-lucide-github', target: '_blank' },
    { label: 'Swagger', to: `${useRuntimeConfig().public.openFetch.api.baseURL}swagger`, icon: 'i-lucide-book-open', target: '_blank' },
]);
</script>
