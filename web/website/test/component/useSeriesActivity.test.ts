import { describe, it, expect, vi, afterEach } from 'vitest';
import { ref, nextTick, effectScope } from 'vue';
import { useSeriesActivity } from '~/composables/useSeriesActivity';

describe('useSeriesActivity', () => {
    afterEach(() => vi.useRealTimers());

    function harness(initial = 0) {
        vi.useFakeTimers();
        const active = ref(initial);
        const poll = vi.fn();
        const onDrained = vi.fn();
        const scope = effectScope();
        scope.run(() => useSeriesActivity(active, { poll, onDrained, intervalMs: 1000 }));
        return { active, poll, onDrained, scope };
    }

    it('polls while jobs are in flight and fires onDrained once when they empty', async () => {
        const { active, poll, onDrained } = harness();

        active.value = 3;
        await nextTick();
        vi.advanceTimersByTime(2500);
        expect(poll).toHaveBeenCalledTimes(2);

        active.value = 0;
        await nextTick();
        expect(onDrained).toHaveBeenCalledTimes(1);

        // Quiet afterwards: no more polling, no repeat firing.
        vi.advanceTimersByTime(5000);
        expect(poll).toHaveBeenCalledTimes(2);
        expect(onDrained).toHaveBeenCalledTimes(1);
    });

    it('does not fire when the series was already idle', async () => {
        const { active, onDrained } = harness(0);

        active.value = 0;
        await nextTick();
        vi.advanceTimersByTime(5000);

        expect(onDrained).not.toHaveBeenCalled();
    });

    it('stops polling when the scope is disposed', async () => {
        const { active, poll, scope } = harness();

        active.value = 1;
        await nextTick();
        scope.stop();
        vi.advanceTimersByTime(5000);

        expect(poll).not.toHaveBeenCalled();
    });
});
