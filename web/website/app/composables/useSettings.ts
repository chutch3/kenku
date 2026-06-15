/** Shared read-state and mutations for the Settings page. Each settings card calls this and uses the
 * slice it needs; the fetches dedupe on their keys, so cards stay self-contained without prop-drilling
 * from the page. The old settings.vue inlined all of this in a single 410-line component. */
export function useSettings() {
    const { $api } = useNuxtApp();
    const toast = useToast();

    const librariesQuery = useApi('/v2/LibraryConnector', { key: FetchKeys.Libraries.All, server: false });
    const settingsQuery = useApi('/v2/Settings', { key: FetchKeys.Settings.All, server: false });
    const statsQuery = useApi('/v2/Stats', { server: false });

    const libraries = librariesQuery.data;
    const settingsData = settingsQuery.data;
    const settingsStatus = settingsQuery.status;
    const stats = statsQuery.data;

    const komgaConnected = computed(() => libraries.value?.find((l) => l.type === 'Komga'));
    const kavitaConnected = computed(() => libraries.value?.find((l) => l.type === 'Kavita'));
    const metronConnected = computed(() => !!settingsData.value?.metronConfigured);
    const apiKey = computed(() => settingsData.value?.apiKey ?? '');
    const syncedIndexers = computed(() => settingsData.value?.syncedIndexers ?? []);
    const downloadClients = computed(() => settingsData.value?.downloadClients ?? []);

    const refreshLibraries = () => refreshNuxtData(FetchKeys.Libraries.All);
    const refreshSettings = () => refreshNuxtData(FetchKeys.Settings.All);

    const disconnectLibrary = async (key: string) => {
        await $api('/v2/LibraryConnector/{LibraryConnectorId}', { method: 'DELETE', path: { LibraryConnectorId: key } });
        await refreshLibraries();
    };
    const regenerateApiKey = async () => {
        await $api('/v2/Settings/ApiKey/Regenerate', { method: 'POST' });
        await refreshSettings();
    };
    const disconnectMetron = async () => {
        await $api('/v2/Settings/Metron', { method: 'DELETE' });
        await refreshSettings();
    };
    const removeDownloadClient = async (id?: number) => {
        if (id === undefined) return;
        await $api('/v2/Settings/DownloadClients/{id}', { method: 'DELETE', path: { id } });
        await refreshSettings();
    };

    const copy = async (value: string) => {
        try {
            await navigator.clipboard.writeText(value);
            toast.add({ title: 'Copied to clipboard', icon: 'i-lucide-copy', color: 'neutral', duration: 1200 });
        } catch {
            toast.add({ title: 'Copy failed', description: 'Clipboard is unavailable here.', icon: 'i-lucide-triangle-alert', color: 'error' });
        }
    };

    return {
        settingsStatus, settingsData, libraries, stats,
        komgaConnected, kavitaConnected, metronConnected, apiKey, syncedIndexers, downloadClients,
        refreshLibraries, refreshSettings, disconnectLibrary, regenerateApiKey, disconnectMetron, removeDownloadClient, copy,
    };
}
