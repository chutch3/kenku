import { describe, it, expect, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import AppVersion from '~/components/AppVersion.vue';

registerEndpoint('/v2/Version', () => ({ version: 'v0.16.0', commit: 'abc1234', builtAt: '2026-06-11 14:00:00Z' }));

describe('AppVersion', () => {
    it('shows the deployed version from the API', async () => {
        const wrapper = await mountSuspended(AppVersion);

        await vi.waitFor(() => expect(wrapper.text()).toContain('v0.16.0'));
    });
});
