import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import { defineComponent } from 'vue';
import CustomThemeBuilder from '~/components/CustomThemeBuilder.vue';

// Wrapper reads the shared theme state so we can assert the builder's effect.
const Wrapper = defineComponent({
    components: { CustomThemeBuilder },
    setup() {
        const { current } = useTheme();
        return { current };
    },
    template: `<div><span class="cur">{{ current }}</span><CustomThemeBuilder /></div>`,
});

describe('CustomThemeBuilder', () => {
    it('exposes three seed inputs and applying makes custom the active theme', async () => {
        const wrapper = await mountSuspended(Wrapper);
        expect(wrapper.findAll('input[type="color"]').length).toBe(3);

        await wrapper.get('[data-test="apply-custom"]').trigger('click');
        expect(wrapper.find('.cur').text()).toBe('custom');
    });

    it('warns when the primary colour is too light to read as text', async () => {
        const wrapper = await mountSuspended(Wrapper);

        await wrapper.get('[data-test="seed-primary"]').setValue('#fff700');
        expect(wrapper.find('[data-test="contrast-warning"]').exists()).toBe(true);

        await wrapper.get('[data-test="seed-primary"]').setValue('#102040');
        expect(wrapper.find('[data-test="contrast-warning"]').exists()).toBe(false);
    });
});
