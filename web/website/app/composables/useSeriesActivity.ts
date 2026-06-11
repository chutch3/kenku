import type { Ref } from 'vue';

/**
 * Watches a series' in-flight job count: polls while work is running and fires onDrained once when
 * the queue empties, so a detail page refreshes itself instead of relying on manual reload.
 */
export function useSeriesActivity(
    active: Ref<number>,
    opts: { poll: () => void; onDrained: () => void; intervalMs?: number },
) {
    let timer: ReturnType<typeof setInterval> | undefined;
    const stop = () => {
        if (timer) clearInterval(timer);
        timer = undefined;
    };
    watch(active, (now, prev) => {
        if (now > 0 && !timer) timer = setInterval(opts.poll, opts.intervalMs ?? 4000);
        if (now === 0) {
            stop();
            if ((prev ?? 0) > 0) opts.onDrained();
        }
    });
    onScopeDispose(stop);
}
