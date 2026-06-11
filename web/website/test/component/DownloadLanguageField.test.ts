import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import DownloadLanguageField from '~/components/DownloadLanguageField.vue';

let patched: string | null = null;

registerEndpoint('/v2/Settings/DownloadLanguage', () => 'en');
registerEndpoint('/v2/Settings/DownloadLanguage/de', {
    method: 'PATCH',
    handler: () => {
        patched = 'de';
        return {};
    },
});

describe('DownloadLanguageField', () => {
    beforeEach(() => {
        patched = null;
        clearNuxtData();
    });

    it('shows the current language and saves a new one', async () => {
        const wrapper = await mountSuspended(DownloadLanguageField);
        await vi.waitFor(() => expect((wrapper.find('input').element as HTMLInputElement).value).toBe('en'));

        await wrapper.find('input').setValue('de');
        await wrapper.find('button').trigger('click');

        await vi.waitFor(() => expect(patched).toBe('de'));
    });
});
