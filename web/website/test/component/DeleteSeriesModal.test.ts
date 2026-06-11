import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import DeleteSeriesModal from '~/components/DeleteSeriesModal.vue';

let deleteCalled = false;
let releaseDelete: (() => void) | null = null;

registerEndpoint('/v2/Series/s1', {
    method: 'DELETE',
    handler: async () => {
        deleteCalled = true;
        await new Promise<void>((resolve) => {
            releaseDelete = resolve;
        });
        return {};
    },
});

function findButton(label: string): HTMLButtonElement {
    const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label));
    expect(button, `button "${label}"`).toBeTruthy();
    return button as HTMLButtonElement;
}

describe('DeleteSeriesModal', () => {
    beforeEach(() => {
        deleteCalled = false;
        releaseDelete = null;
        document.body.innerHTML = '';
    });

    it('asks before deleting and shows the request in flight until the API answers', async () => {
        const wrapper = await mountSuspended(DeleteSeriesModal, {
            props: { mangaId: 's1', seriesName: 'The Boys', open: true },
        });

        // Confirmation first — nothing fires on open.
        expect(deleteCalled).toBe(false);
        expect(document.body.textContent).toContain('The Boys');

        findButton('Delete').click();
        await vi.waitFor(() => expect(deleteCalled).toBe(true));

        // In flight: no completion yet, and the action can't be double-fired.
        expect(wrapper.emitted('deleted')).toBeFalsy();
        await vi.waitFor(() => expect(findButton('Delete').disabled).toBe(true));

        releaseDelete!();
        await vi.waitFor(() => expect(wrapper.emitted('deleted')).toBeTruthy());
    });

    it('cancel closes without calling the API', async () => {
        await mountSuspended(DeleteSeriesModal, {
            props: { mangaId: 's1', seriesName: 'The Boys', open: true },
        });

        findButton('Cancel').click();

        expect(deleteCalled).toBe(false);
    });
});
