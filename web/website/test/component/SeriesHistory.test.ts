import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import SeriesHistory from '~/components/SeriesHistory.vue';

registerEndpoint('/v2/Actions/Filter', {
    method: 'POST',
    handler: () => ({
        data: [
            { key: 'a1', action: 'ChapterDownloaded', performedAt: '2026-06-19T00:00:00Z', seriesId: 'm1', chapterId: 'c1' },
            { key: 'a2', action: 'MetadataUpdated', performedAt: '2026-06-19T01:00:00Z', seriesId: 'm1', chapterId: null },
        ],
        totalCount: 2,
    }),
});

describe('SeriesHistory', () => {
    beforeEach(() => clearNuxtData());

    it('lists the recorded events for the series in readable form', async () => {
        const wrapper = await mountSuspended(SeriesHistory, { props: { seriesId: 'm1' } });
        await vi.waitFor(() => {
            expect(wrapper.text()).toContain('Chapter Downloaded');
            expect(wrapper.text()).toContain('Metadata Updated');
        });
    });
});
