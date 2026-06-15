<template>
    <div class="flex flex-row flex-wrap gap-2">
        <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="gotify.open()">Gotify</UButton>
        <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="ntfy.open()">Ntfy</UButton>
        <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="pushover.open()">Pushover</UButton>
        <UButton icon="i-lucide-plus" variant="soft" class="w-fit" @click="generic.open()">Generic webhook</UButton>
    </div>
</template>

<script setup lang="ts">
import { LazyGotifyModal, LazyNtfyModal, LazyPushoverModal, LazyGenericNotificationConnectorModal } from '#components';

const overlay = useOverlay();
const gotify = overlay.create(LazyGotifyModal);
const ntfy = overlay.create(LazyNtfyModal);
const pushover = overlay.create(LazyPushoverModal);
const generic = overlay.create(LazyGenericNotificationConnectorModal);
</script>
