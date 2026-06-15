import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, mockNuxtImport } from '@nuxt/test-utils/runtime';
import { defineComponent } from 'vue';
import type { components } from '#open-fetch-schemas/api';

type MinimalSeries = components['schemas']['MinimalSeries'];

const { toastAdd } = vi.hoisted(() => ({ toastAdd: vi.fn() }));
mockNuxtImport('useToast', () => () => ({ add: toastAdd, remove: vi.fn(), clear: vi.fn(), update: vi.fn() }));

function series(): MinimalSeries {
    return {
        key: 's1', name: 'Berserk', description: '', releaseStatus: 'Continuing',
        sourceIds: [{ key: 'sid', mangaConnectorName: 'WeebCentral', objId: 's1', idOnConnectorSite: 'x', websiteUrl: null, useForDownload: false }],
        fileLibraryId: null, originalLanguage: 'en', coverUrl: '',
    } as MinimalSeries;
}

// Expose the composable's API off a mounted instance so Nuxt context is present.
let flow: ReturnType<typeof useAddSeriesFlow>;
const Harness = defineComponent({
    setup() {
        flow = useAddSeriesFlow();
        return () => null;
    },
});

describe('useAddSeriesFlow', () => {
    beforeEach(() => toastAdd.mockClear());

    it('startAdd stages the series and opens the modal', async () => {
        await mountSuspended(Harness);
        flow.startAdd(series());
        expect(flow.pendingAdd.value?.name).toBe('Berserk');
        expect(flow.addModalOpen.value).toBe(true);
    });

    it('notifyAdded flips the result in place and toasts a downloading message', async () => {
        await mountSuspended(Harness);
        const s = series();
        flow.notifyAdded(s, { libraryId: 'lib1', download: true });

        expect(s.fileLibraryId).toBe('lib1');
        expect(s.sourceIds[0]!.useForDownload).toBe(true);
        expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ title: 'Added Berserk — downloading', color: 'success' }));
    });

    it('notifyAdded toasts a plain added message when not downloading', async () => {
        await mountSuspended(Harness);
        flow.notifyAdded(series(), { libraryId: 'lib1', download: false });
        expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ title: 'Added Berserk' }));
    });
});
