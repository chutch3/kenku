import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import { defineComponent } from 'vue';

// A tiny harness exercises the composable in a real Nuxt context (useState/useCookie).
const Harness = defineComponent({
    setup() {
        const { current, set, themes } = useTheme();
        return { current, set, count: themes.length };
    },
    template: `<div>
        <span class="cur">{{ current }}</span>
        <span class="count">{{ count }}</span>
        <button class="to-pride" @click="set('trans-pride')">p</button>
        <button class="to-bogus" @click="set('not-real')">b</button>
    </div>`,
});

describe('useTheme', () => {
    it('defaults to karasu, ignores unknown ids, and switches to known themes', async () => {
        const wrapper = await mountSuspended(Harness);
        expect(wrapper.find('.cur').text()).toBe('karasu');
        expect(Number(wrapper.find('.count').text())).toBeGreaterThan(1);

        await wrapper.find('.to-bogus').trigger('click');
        expect(wrapper.find('.cur').text()).toBe('karasu'); // unknown id ignored

        await wrapper.find('.to-pride').trigger('click');
        expect(wrapper.find('.cur').text()).toBe('trans-pride');
    });
});
