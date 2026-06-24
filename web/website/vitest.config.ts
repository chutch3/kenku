import { defineVitestConfig } from '@nuxt/test-utils/config';

// Component tests run in the Nuxt environment so composables (useApi/$api from nuxt-open-fetch),
// auto-imports and @nuxt/ui components resolve exactly as they do at runtime. test/unit holds
// environment-agnostic pure-logic tests (e.g. the theme generator). Neither glob picks up the
// node-based api-contract test.
export default defineVitestConfig({
    test: {
        environment: 'nuxt',
        include: ['test/component/**/*.test.ts', 'test/unit/**/*.test.ts'],
    },
});
