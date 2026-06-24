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

const CustomHarness = defineComponent({
    setup() {
        const { current, setCustom, customSeeds } = useTheme();
        return {
            current,
            primary: computed(() => customSeeds.value?.primary ?? ''),
            apply: () => setCustom({ primary: '#112233', secondary: '#445566', neutral: '#778899' }),
        };
    },
    template: `<div>
        <span class="cur">{{ current }}</span>
        <span class="primary">{{ primary }}</span>
        <button class="apply" @click="apply">a</button>
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

    it('applies and persists a custom theme from user seeds', async () => {
        const wrapper = await mountSuspended(CustomHarness);
        await wrapper.find('.apply').trigger('click');
        expect(wrapper.find('.cur').text()).toBe('custom');
        expect(wrapper.find('.primary').text()).toBe('#112233');
    });
});
