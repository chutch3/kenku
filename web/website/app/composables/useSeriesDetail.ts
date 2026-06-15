import type { components } from '#open-fetch-schemas/api';

type Series = components['schemas']['Series'];

/** Data + mutations for the series detail page. The series is a computed off the fetch (no manual ref
 * mirror), and every fetch is lazy + client-only, so there are no awaits to lose the Nuxt context. */
export function useSeriesDetail(mangaId: string) {
    const { $api } = useNuxtApp();
    const toast = useToast();

    const rollupQuery = useApi('/v2/Series/Rollup', { key: FetchKeys.Series.Rollup, lazy: true, server: false });
    const connectorsQuery = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, lazy: true, server: false });
    const seriesQuery = useApi('/v2/Series/{MangaId}', {
        path: { MangaId: mangaId },
        key: FetchKeys.Series.Id(mangaId),
        onResponseError: () => navigateTo('/'),
        lazy: true,
        server: false,
    });

    const series = computed<Series | null>(() => seriesQuery.data.value ?? null);
    const rollup = computed(() => (rollupQuery.data.value ?? []).find((r) => r.mangaId === mangaId) ?? null);
    const kind = computed<SeriesKind>(() => (series.value ? seriesKind(series.value, connectorsQuery.data.value) : 'manga'));

    const refreshRollups = () => rollupQuery.refresh();
    onMounted(refreshRollups);

    const setRequestedFrom = async (mangaConnectorName: string, isRequested: boolean) => {
        await $api('/v2/Series/{MangaId}/DownloadFrom/{MangaConnectorName}/{IsRequested}', {
            method: 'PATCH',
            path: { MangaId: mangaId, MangaConnectorName: mangaConnectorName, IsRequested: isRequested },
        });
        await refreshNuxtData(FetchKeys.Series.Id(mangaId));
    };

    const syncNow = async () => {
        await $api('/v2/Series/{MangaId}/Sync', { method: 'POST', path: { MangaId: mangaId } });
        toast.add({ title: 'Sync queued', description: 'Chapters and cover refresh from your sources.', icon: 'i-lucide-cloud-download', color: 'success' });
        await refreshRollups();
    };

    const refreshingData = ref(false);
    const refreshData = async (quiet = false) => {
        refreshingData.value = true;
        await refreshNuxtData([
            FetchKeys.Series.Id(mangaId),
            FetchKeys.Series.Rollup,
            FetchKeys.Metadata.Series(mangaId),
            FetchKeys.FileLibraries,
            FetchKeys.Chapters.Series(mangaId),
        ]);
        refreshingData.value = false;
        if (!quiet) toast.add({ title: 'Series refreshed', icon: 'i-lucide-check', color: 'neutral', duration: 1500 });
    };

    // While jobs for this series are in flight, poll the rollup and refresh the page when they drain.
    const activeJobs = computed(() => (rollup.value ? rollup.value.queuedJobs + rollup.value.runningJobs : 0));
    useSeriesActivity(activeJobs, { poll: refreshRollups, onDrained: () => refreshData(true) });

    return { series, rollup, kind, refreshingData, refreshData, refreshRollups, setRequestedFrom, syncNow };
}
