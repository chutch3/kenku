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

    it('groups the buttons with an accessible label for screen readers', async () => {
        const wrapper = await mountSuspended(MediaModeToggle);
        const group = wrapper.find('[role="group"]');
        expect(group.exists()).toBe(true);
        expect(group.attributes('aria-label')?.toLowerCase()).toContain('manga');
    });

    it("describes its real scope — search — and does not claim to filter the library", async () => {
        // The toggle drives only search (the library has its own independent filter); the tooltip must
        // not over-claim, which is exactly how a misleading "filters your library" label slipped in once.
        const wrapper = await mountSuspended(MediaModeToggle);
        const tip = wrapper.find('[title]').attributes('title') ?? '';
        expect(tip.toLowerCase()).toContain('search');
        expect(tip.toLowerCase()).not.toContain('library');
    });
});
