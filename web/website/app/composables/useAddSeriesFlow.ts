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

    const onAdded = ({ libraryId, download }: { libraryId: string; download: boolean }) => {
        const added = pendingAdd.value;
        if (!added) return;
        // Flip the result in place so its card shows the real tracked state without refetching.
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

    return { pendingAdd, addModalOpen, startAdd, onAdded };
};
