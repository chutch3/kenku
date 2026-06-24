import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import ThemePreview from '~/components/ThemePreview.vue';

describe('ThemePreview', () => {
    it('scopes itself with data-theme so the catalog CSS paints the real surfaces', async () => {
        const wrapper = await mountSuspended(ThemePreview, { props: { themeId: 'trans-pride' } });
        expect(wrapper.find('[data-theme="trans-pride"]').exists()).toBe(true);
        // a primary accent chip is part of the preview
        expect(wrapper.find('[data-test="preview-accent"]').exists()).toBe(true);
    });
});
