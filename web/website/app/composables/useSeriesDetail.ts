import type { components } from '#open-fetch-schemas/api';

type Series = components['schemas']['Series'];

/** Data + mutations for the series detail page. The series is a computed off the fetch (no manual ref
 * mirror), and every fetch is lazy + client-only, so there are no awaits to lose the Nuxt context. */
export function useSeriesDetail(seriesId: string) {
    const { $api } = useNuxtApp();
    const toast = useToast();

    const rollupQuery = useApi('/v2/Series/Rollup', { key: FetchKeys.Series.Rollup, lazy: true, server: false });
    const connectorsQuery = useApi('/v2/SeriesSource', { key: FetchKeys.MangaConnector.All, lazy: true, server: false });
    const seriesQuery = useApi('/v2/Series/{SeriesId}', {
        path: { SeriesId: seriesId },
        key: FetchKeys.Series.Id(seriesId),
        onResponseError: () => navigateTo('/'),
        lazy: true,
        server: false,
    });

    const series = computed<Series | null>(() => seriesQuery.data.value ?? null);
    const rollup = computed(() => (rollupQuery.data.value ?? []).find((r) => r.seriesId === seriesId) ?? null);
    const kind = computed<SeriesKind>(() => (series.value ? seriesKind(series.value, connectorsQuery.data.value) : 'manga'));

    const refreshRollups = () => rollupQuery.refresh();
    onMounted(refreshRollups);

    const setRequestedFrom = async (seriesSourceName: string, isRequested: boolean) => {
        await $api('/v2/Series/{SeriesId}/DownloadFrom/{SeriesSourceName}/{IsRequested}', {
            method: 'PATCH',
            path: { SeriesId: seriesId, SeriesSourceName: seriesSourceName, IsRequested: isRequested },
        });
        await refreshNuxtData(FetchKeys.Series.Id(seriesId));
    };

    const syncNow = async () => {
        await $api('/v2/Series/{SeriesId}/Sync', { method: 'POST', path: { SeriesId: seriesId } });
        toast.add({ title: 'Sync queued', description: 'Chapters and cover refresh from your sources.', icon: 'i-lucide-cloud-download', color: 'success' });
        await refreshRollups();
    };

    const refreshingData = ref(false);
    const refreshData = async (quiet = false) => {
        refreshingData.value = true;
        await refreshNuxtData([
            FetchKeys.Series.Id(seriesId),
            FetchKeys.Series.Rollup,
            FetchKeys.Metadata.Series(seriesId),
            FetchKeys.FileLibraries,
            FetchKeys.Chapters.Series(seriesId),
        ]);
        refreshingData.value = false;
        if (!quiet) toast.add({ title: 'Series refreshed', icon: 'i-lucide-check', color: 'neutral', duration: 1500 });
    };

    // While jobs for this series are in flight, poll the rollup and refresh the page when they drain.
    const activeJobs = computed(() => (rollup.value ? rollup.value.queuedJobs + rollup.value.runningJobs : 0));
    useSeriesActivity(activeJobs, { poll: refreshRollups, onDrained: () => refreshData(true) });

    return { series, rollup, kind, refreshingData, refreshData, refreshRollups, setRequestedFrom, syncNow };
}
