import type { components } from '#open-fetch-schemas/api';
type MinimalSeries = components['schemas']['MinimalSeries'];

/** Owns the AddSeriesModal hand-off and the post-add refresh + toast, shared by search and Discover. */
export const useAddSeriesFlow = () => {
    const pendingAdd = ref<MinimalSeries | null>(null);
    const addModalOpen = ref(false);
    const toast = useToast();

    const startAdd = (series: MinimalSeries) => {
        pendingAdd.value = series;
        addModalOpen.value = true;
    };

    /** Post-add bookkeeping shared by every add entry point: refresh, toast, flip the result in place. */
    const notifyAdded = (added: MinimalSeries, { libraryId, download }: { libraryId: string; download: boolean }) => {
        added.fileLibraryId = libraryId;
        if (added.sourceIds[0]) added.sourceIds[0].useForDownload = download;
        refreshNuxtData([FetchKeys.Series.All, FetchKeys.Series.Rollup]);
        toast.add({
            title: download ? `Added ${added.name} — downloading` : `Added ${added.name}`,
            icon: download ? 'i-lucide-cloud-download' : 'i-lucide-bookmark-plus',
            color: 'success',
            actions: [{ label: 'Open series', onClick: () => void navigateTo(`/series/${added.key}`) }],
        });
    };

    const onAdded = (payload: { libraryId: string; download: boolean }) => {
        if (pendingAdd.value) notifyAdded(pendingAdd.value, payload);
    };

    return { pendingAdd, addModalOpen, startAdd, onAdded, notifyAdded };
};
