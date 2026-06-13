import { describe, it, expect } from 'vitest';
import { mountSuspended } from '@nuxt/test-utils/runtime';
import DiscoveryRail from '~/components/DiscoveryRail.vue';

const entries = [
    { title: 'Berserk', coverUrl: 'http://c/1.jpg', url: 'https://anilist.co/manga/1', source: 'AniList', blurb: null },
    { title: 'Pick Me Up', coverUrl: 'http://c/2.jpg', url: 'https://anilist.co/manga/2', source: 'AniList', blurb: 'gacha' },
];
const library = [
    { key: 's1', name: 'berserk', description: '', releaseStatus: 'Continuing', sourceIds: [], fileLibraryId: 'lib1', originalLanguage: 'en', coverUrl: '' },
];

const cardFor = (wrapper: Awaited<ReturnType<typeof mountSuspended>>, title: string) =>
    wrapper.findAll('button').find((b) => b.text().includes(title))!;

describe('DiscoveryRail', () => {
    it('marks entries already in the library and routes them to their series page', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending manga', entries, library } });

        expect(wrapper.text()).toContain('Berserk');
        expect(wrapper.text()).toContain('In library');

        await cardFor(wrapper, 'Berserk').trigger('click');
        expect(wrapper.emitted('open')).toEqual([['s1']]);
    });

    it('routes new entries to the add flow', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending manga', entries, library } });

        await cardFor(wrapper, 'Pick Me Up').trigger('click');
        const picked = wrapper.emitted('pick')!;
        expect((picked[0]![0] as { title: string }).title).toBe('Pick Me Up');
        expect(wrapper.emitted('open')).toBeFalsy();
    });

    it('orders fresh entries ahead of ones already in the library', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending', entries, library } });

        const titles = wrapper.findAll('button').map((b) => b.text());
        // "Pick Me Up" (new) comes before "Berserk" (owned) — discovery leads with what you don't have.
        expect(titles.findIndex((t) => t.includes('Pick Me Up'))).toBeLessThan(titles.findIndex((t) => t.includes('Berserk')));
    });

    it('drops entries already shown by an earlier rail', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, {
            props: { title: 'Top rated', entries, exclude: ['berserk'] },
        });

        expect(wrapper.text()).toContain('Pick Me Up');
        expect(wrapper.text()).not.toContain('Berserk');
    });

    it('external rails link out and signal that they leave the app', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'From the feeds', entries, external: true } });

        const link = wrapper.find('a');
        expect(link.attributes('href')).toBe('https://anilist.co/manga/1');
        expect(link.attributes('target')).toBe('_blank');
        const icons = wrapper.findAllComponents({ name: 'UIcon' }).map((c) => c.props('name'));
        expect(icons).toContain('i-lucide-external-link');
        expect(wrapper.text()).not.toContain('In library');
    });

    it('caps a rail at twelve entries so it stays a browse, not a wall', async () => {
        const many = Array.from({ length: 15 }, (_, i) => ({
            title: `Title ${i}`, coverUrl: '', url: `https://anilist.co/manga/${i}`, source: 'AniList', blurb: null,
        }));
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending', entries: many } });

        expect(wrapper.findAll('button')).toHaveLength(12);
    });

    it('renders nothing while empty', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending manga', entries: [] } });

        expect(wrapper.text()).toBe('');
    });
});
