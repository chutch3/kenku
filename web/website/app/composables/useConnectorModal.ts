/** Shared submit flow for the connector/library/download-client modals. Each one used to repeat the
 * same success-ref + refresh + empty-catch + shake scaffold, swallowing API errors silently. This
 * centralises it: a failed save now raises a toast instead of a 200ms shake-and-nothing. */
export interface ConnectorModalOptions {
    /** The mutation call (PUT/POST/PATCH) — typically a thin `$api(...)` wrapper. */
    action: () => Promise<unknown>;
    /** Fetch key(s) to invalidate after a successful save. */
    refreshKeys: string | string[];
    /** Toast title shown on success, e.g. "Gotify connected". */
    successTitle: string;
    /** Close the overlay (each modal emits its own `close`). */
    onClose: () => void;
}

export function useConnectorModal(opts: ConnectorModalOptions) {
    const toast = useToast();
    // `false` briefly drives the shake-on-error animation on the submit button; back to undefined after.
    const success = ref<boolean | undefined>(undefined);

    const submit = async () => {
        try {
            await opts.action();
            await refreshNuxtData(opts.refreshKeys);
            success.value = true;
            toast.add({ title: opts.successTitle, icon: 'i-lucide-check', color: 'success' });
            opts.onClose();
        } catch (error) {
            success.value = false;
            toast.add({
                title: 'Connection failed',
                description: connectorErrorMessage(error),
                icon: 'i-lucide-triangle-alert',
                color: 'error',
            });
            setTimeout(() => (success.value = undefined), 200);
        }
    };

    return { success, submit };
}

/** Pull a human message off an ofetch error, falling back to a generic hint. */
function connectorErrorMessage(error: unknown): string {
    if (error && typeof error === 'object') {
        const e = error as { data?: { message?: string }; statusMessage?: string; message?: string };
        return e.data?.message ?? e.statusMessage ?? e.message ?? 'Check the details and try again.';
    }
    return 'Check the details and try again.';
}
