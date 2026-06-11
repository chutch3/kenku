import { describe, it, expect, vi, afterEach } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import IndexerCooldownBadge from '~/components/IndexerCooldownBadge.vue';
import { tooltipStub } from './tooltipStub';

describe('IndexerCooldownBadge', () => {
    afterEach(() => vi.useRealTimers());

    it('counts down and disappears when the cooldown elapses', async () => {
        vi.useFakeTimers();
        const until = new Date(Date.now() + 90_000).toISOString();

        const wrapper = await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: until }, global: { stubs: tooltipStub } });
        expect(wrapper.text()).toContain('2m');

        await vi.advanceTimersByTimeAsync(2 * 60_000);
        expect(wrapper.text()).toBe('');
    });

    it('flags a rate-limited indexer with the time until retry', async () => {
        const until = new Date(Date.now() + 9 * 60_000).toISOString();

        const wrapper = await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: until }, global: { stubs: tooltipStub } });

        expect(wrapper.text().toLowerCase()).toContain('rate-limited');
        expect(wrapper.text()).toContain('9m');
    });

    it('renders nothing when the indexer is free to search', async () => {
        const past = new Date(Date.now() - 60_000).toISOString();

        expect((await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: null }, global: { stubs: tooltipStub } })).text()).toBe('');
        expect((await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: past }, global: { stubs: tooltipStub } })).text()).toBe('');
    });
});
