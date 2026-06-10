import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { readBody } from 'h3';
import RematchSourceModal from '~/components/RematchSourceModal.vue';

const source = { key: 'src-old', mangaConnectorName: 'WeebCentral', objId: 's1', idOnConnectorSite: '01BAD/Wrong-Slug', websiteUrl: null, useForDownload: true };

let rematchBody: Record<string, string> | null = null;

registerEndpoint('/v2/Search/WeebCentral/I Am A Hero', () => [
    {
        key: 'result-1',
        name: 'I Am A Hero',
        description: '',
        releaseStatus: 'Completed',
        sourceIds: [{ key: 'sid-good', mangaConnectorName: 'WeebCentral', objId: 'x', idOnConnectorSite: '01ABC', websiteUrl: 'https://weebcentral.com/series/01ABC', useForDownload: false }],
        fileLibraryId: null,
        originalLanguage: 'en',
        coverUrl: '',
    },
]);
registerEndpoint('/v2/Series/s1/Source/src-old/Rematch', {
    method: 'POST',
    handler: async (event) => {
        rematchBody = await readBody(event);
        return {};
    },
});

function findButton(label: string): HTMLButtonElement {
    const button = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label));
    expect(button, `button "${label}"`).toBeTruthy();
    return button as HTMLButtonElement;
}

describe('RematchSourceModal', () => {
    beforeEach(() => {
        rematchBody = null;
        document.body.innerHTML = '';
    });

    it('searches the connector and re-links to the picked entry', async () => {
        const wrapper = await mountSuspended(RematchSourceModal, {
            props: { mangaId: 's1', source, seriesName: 'I Am A Hero', open: true },
        });
        await vi.waitFor(() => findButton('Search'));

        findButton('Search').click();
        await vi.waitFor(() => expect(document.body.textContent).toContain('01ABC'));

        findButton('Use this').click();

        await vi.waitFor(() =>
            expect(rematchBody).toMatchObject({
                idOnConnectorSite: '01ABC',
                websiteUrl: 'https://weebcentral.com/series/01ABC',
            }));
        expect(wrapper.emitted('rematched')).toBeTruthy();
    });
});
