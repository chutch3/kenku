import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint, mockNuxtImport } from '@nuxt/test-utils/runtime';
import { createError } from 'h3';
import KavitaModal from '~/components/KavitaModal.vue';

// useConnectorModal is exercised through a real modal (KavitaModal) so the wiring — action, refresh,
// toast, close — is tested end to end. KavitaModal has the simplest form (url + apiKey).
const { toastAdd } = vi.hoisted(() => ({ toastAdd: vi.fn() }));
mockNuxtImport('useToast', () => () => ({ add: toastAdd, remove: vi.fn(), clear: vi.fn(), update: vi.fn() }));

let shouldFail = false;
registerEndpoint('/v2/LibraryConnector/Kavita', {
    method: 'PUT',
    handler: () => {
        if (shouldFail) throw createError({ statusCode: 500, statusMessage: 'Kavita unreachable' });
        return {};
    },
});

function inputs(): HTMLInputElement[] {
    return [...document.body.querySelectorAll('input')] as HTMLInputElement[];
}
function setInput(el: HTMLInputElement, value: string) {
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
}
function findButton(label: string): HTMLButtonElement {
    const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label));
    expect(button, `button "${label}"`).toBeTruthy();
    return button as HTMLButtonElement;
}

describe('useConnectorModal (via KavitaModal)', () => {
    beforeEach(() => {
        shouldFail = false;
        toastAdd.mockClear();
        document.body.innerHTML = '';
    });

    it('saves, refreshes, toasts success and closes', async () => {
        const wrapper = await mountSuspended(KavitaModal, { props: { open: true } });
        const [url, apiKey] = inputs();
        setInput(url!, 'https://kavita.local');
        setInput(apiKey!, 'secret-key');
        await vi.waitFor(() => expect(findButton('Connect').disabled).toBe(false));

        findButton('Connect').click();

        await vi.waitFor(() => expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ color: 'success' })));
        expect(wrapper.emitted('close')).toBeTruthy();
    });

    it('surfaces a failed save as an error toast and does not close', async () => {
        shouldFail = true;
        const wrapper = await mountSuspended(KavitaModal, { props: { open: true } });
        const [url, apiKey] = inputs();
        setInput(url!, 'https://kavita.local');
        setInput(apiKey!, 'secret-key');
        await vi.waitFor(() => expect(findButton('Connect').disabled).toBe(false));

        findButton('Connect').click();

        await vi.waitFor(() => expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ color: 'error' })));
        expect(wrapper.emitted('close')).toBeFalsy();
    });
});
