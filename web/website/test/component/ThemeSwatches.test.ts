import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import ThemeSwatches from '~/components/ThemeSwatches.vue';

describe('ThemeSwatches', () => {
    it('renders one swatch per seed colour', async () => {
        const wrapper = await mountSuspended(ThemeSwatches, {
            props: { seeds: { primary: '#112233', secondary: '#445566', neutral: '#778899' } },
        });
        const swatches = wrapper.findAll('[data-test="swatch"]');
        expect(swatches.length).toBe(3);
        // Three distinct backgrounds — confirms each seed is mapped to its own swatch.
        const styles = swatches.map((s) => s.attributes('style') ?? '');
        expect(new Set(styles).size).toBe(3);
    });
});
