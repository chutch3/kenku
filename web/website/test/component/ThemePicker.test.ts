import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import ThemePicker from '~/components/ThemePicker.vue';

describe('ThemePicker', () => {
    it('lists themes grouped by package, marks the current one, and switches on click', async () => {
        const wrapper = await mountSuspended(ThemePicker);

        // The default (karasu) is shown as selected.
        expect(wrapper.get('[data-test="theme-karasu"]').attributes('aria-pressed')).toBe('true');
        // The trans-pride pilot is listed, under a package grouping.
        expect(wrapper.find('[data-test="theme-trans-pride"]').exists()).toBe(true);
        expect(wrapper.text()).toContain('Pride');

        // Selecting another theme moves the selection.
        await wrapper.get('[data-test="theme-trans-pride"]').trigger('click');
        expect(wrapper.get('[data-test="theme-trans-pride"]').attributes('aria-pressed')).toBe('true');
        expect(wrapper.get('[data-test="theme-karasu"]').attributes('aria-pressed')).toBe('false');
    });
});
