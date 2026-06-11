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

describe('DiscoveryRail', () => {
    it('marks entries already in the library and routes them to their series page', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, {
            props: { title: 'Trending manga', entries, library },
        });

        expect(wrapper.text()).toContain('Berserk');
        expect(wrapper.text()).toContain('In library');

        await wrapper.findAll('button')[0]!.trigger('click');
        expect(wrapper.emitted('open')).toEqual([['s1']]);
    });

    it('routes new entries to the add flow', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, {
            props: { title: 'Trending manga', entries, library },
        });

        await wrapper.findAll('button')[1]!.trigger('click');
        const picked = wrapper.emitted('pick')!;
        expect((picked[0]![0] as { title: string }).title).toBe('Pick Me Up');
        expect(wrapper.emitted('open')).toBeFalsy();
    });

    it('external rails link out instead of emitting', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, {
            props: { title: 'From the feeds', entries, external: true },
        });

        const link = wrapper.find('a');
        expect(link.attributes('href')).toBe('https://anilist.co/manga/1');
        expect(link.attributes('target')).toBe('_blank');
        expect(wrapper.text()).not.toContain('In library');
    });

    it('renders nothing while empty', async () => {
        const wrapper = await mountSuspended(DiscoveryRail, { props: { title: 'Trending manga', entries: [] } });

        expect(wrapper.text()).toBe('');
    });
});
