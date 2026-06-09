import { defineConfig } from '@playwright/test';

// Browser golden flows against the built app. The API is stubbed per-test with page.route(), so these
// prove the real browser journey (pages, modals, toasts) without a backend. Build first: `npm run build`.
export default defineConfig({
    testDir: 'test/e2e',
    timeout: 30_000,
    use: { baseURL: 'http://localhost:4173' },
    webServer: {
        command: 'PORT=4173 node .output/server/index.mjs',
        url: 'http://localhost:4173',
        reuseExistingServer: true,
        timeout: 60_000,
    },
});
