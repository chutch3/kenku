import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import IndexerCooldownBadge from '~/components/IndexerCooldownBadge.vue';

describe('IndexerCooldownBadge', () => {
    it('flags a rate-limited indexer with the time until retry', async () => {
        const until = new Date(Date.now() + 9 * 60_000).toISOString();

        const wrapper = await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: until }, global: { stubs: { UTooltip: { template: '<div><slot /></div>' } } } });

        expect(wrapper.text().toLowerCase()).toContain('rate-limited');
        expect(wrapper.text()).toContain('9m');
    });

    it('renders nothing when the indexer is free to search', async () => {
        const past = new Date(Date.now() - 60_000).toISOString();

        expect((await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: null }, global: { stubs: { UTooltip: { template: '<div><slot /></div>' } } } })).text()).toBe('');
        expect((await mountSuspended(IndexerCooldownBadge, { props: { cooldownUntil: past }, global: { stubs: { UTooltip: { template: '<div><slot /></div>' } } } })).text()).toBe('');
    });
});
