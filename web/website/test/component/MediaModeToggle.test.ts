import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import MediaModeToggle from '~/components/MediaModeToggle.vue';

describe('MediaModeToggle', () => {
    it('defaults to manga and switches to comics on click', async () => {
        const wrapper = await mountSuspended(MediaModeToggle);
        const manga = wrapper.findAll('button').find((b) => b.text() === 'Manga')!;
        const comics = wrapper.findAll('button').find((b) => b.text() === 'Comics')!;

        expect(manga.attributes('aria-pressed')).toBe('true');
        expect(comics.attributes('aria-pressed')).toBe('false');

        await comics.trigger('click');

        expect(comics.attributes('aria-pressed')).toBe('true');
        expect(manga.attributes('aria-pressed')).toBe('false');
    });
});
