import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import { defineComponent } from 'vue';

// A tiny harness exercises the composable in a real Nuxt context (useState/useCookie).
const Harness = defineComponent({
    setup() {
        const { mode, setMode, contentType } = useMediaMode();
        return { mode, setMode, contentType };
    },
    template: `<div>
        <span class="mode">{{ mode }}</span>
        <span class="ct">{{ contentType }}</span>
        <button class="to-comic" @click="setMode('comic')">c</button>
    </div>`,
});

describe('useMediaMode', () => {
    it('defaults to manga and maps each mode to the API content type', async () => {
        const wrapper = await mountSuspended(Harness);
        expect(wrapper.find('.mode').text()).toBe('manga');
        expect(wrapper.find('.ct').text()).toBe('Manga');

        await wrapper.find('.to-comic').trigger('click');

        expect(wrapper.find('.mode').text()).toBe('comic');
        expect(wrapper.find('.ct').text()).toBe('Comic');
    });
});
