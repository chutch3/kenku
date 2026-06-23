import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { readBody } from 'h3';
import ManualIndexerModal from '~/components/ManualIndexerModal.vue';

let posted: Record<string, unknown> | null = null;

registerEndpoint('/v2/Settings/ManualIndexers', {
    method: 'POST',
    handler: async (event) => { posted = await readBody(event); return {}; },
});

// The modal teleports to <body>; query there.
const setInput = (placeholder: string, value: string) => {
    const el = document.body.querySelector<HTMLInputElement>(`input[placeholder="${placeholder}"]`);
    expect(el, `input "${placeholder}"`).toBeTruthy();
    el!.value = value;
    el!.dispatchEvent(new Event('input', { bubbles: true }));
};
const findButton = (label: string) =>
    [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as HTMLButtonElement | undefined;
const clickEnabled = async (label: string) => {
    const btn = await vi.waitFor(() => {
        const b = findButton(label);
        expect(b, `button "${label}"`).toBeTruthy();
        expect(b!.disabled, `button "${label}" enabled`).toBe(false);
        return b!;
    });
    btn.click();
};

describe('ManualIndexerModal', () => {
    beforeEach(() => {
        posted = null;
        document.body.innerHTML = '';
    });

    it('adds an indexer — POSTs name, url, key and parsed categories', async () => {
        await mountSuspended(ManualIndexerModal, { props: { indexer: null, open: true } });
        await vi.waitFor(() => expect(document.body.querySelector('input[placeholder="Nyaa"]')).toBeTruthy());

        setInput('Nyaa', 'AnimeBytes');
        setInput('http://indexer/api', 'http://ab.test/api');
        setInput('7030, 7000', '7030, 7000');
        // API key input has no static placeholder in add mode — set it by type.
        const keyInput = document.body.querySelector<HTMLInputElement>('input[type="password"]')!;
        keyInput.value = 'secret';
        keyInput.dispatchEvent(new Event('input', { bubbles: true }));

        await clickEnabled('Save');

        await vi.waitFor(() => expect(posted).toMatchObject({
            name: 'AnimeBytes', url: 'http://ab.test/api', apiKey: 'secret', categories: [7030, 7000],
        }));
    });

    it('edits — pre-fills url/categories, locks the name, and a blank key is sent (kept server-side)', async () => {
        await mountSuspended(ManualIndexerModal, {
            props: { indexer: { name: 'Nyaa', url: 'http://nyaa.test/api', categories: [7030], cooldownUntil: null }, open: true },
        });
        await vi.waitFor(() => expect(document.body.querySelector('input[placeholder="Nyaa"]')).toBeTruthy());

        const nameInput = document.body.querySelector<HTMLInputElement>('input[placeholder="Nyaa"]')!;
        expect(nameInput.value).toBe('Nyaa');
        expect(nameInput.disabled).toBe(true); // name keys the indexer (upsert), so it is locked on edit

        setInput('http://indexer/api', 'http://nyaa.test/v2');
        await clickEnabled('Save');

        await vi.waitFor(() => expect(posted).toMatchObject({
            name: 'Nyaa', url: 'http://nyaa.test/v2', apiKey: '', categories: [7030],
        }));
    });
});
