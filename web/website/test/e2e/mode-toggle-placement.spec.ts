import { test, expect, type Page } from '@playwright/test';

// The manga/comics toggle only scopes the Add-Series search, so it belongs on that page — not in the
// global header where it implies an app-wide effect it no longer has.
async function stub(page: Page) {
    await page.route('**/v2/SeriesSource', (r) => r.fulfill({ json: [{ key: 'Global', name: 'Global', enabled: true, iconUrl: '', supportedLanguages: ['all'], kind: 'ImageList', contentType: 'Manga' }] }));
    await page.route('**/v2/Series', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/Series/Rollup', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/JobQueue', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/Stats', (r) => r.fulfill({ json: {} }));
    await page.route('**/v2/Version', (r) => r.fulfill({ json: { version: 'test' } }));
    await page.route('**/v2/FileLibrary', (r) => r.fulfill({ json: [] }));
}

test('the manga/comics toggle lives on the Add Series page, not the global header', async ({ page }) => {
    await stub(page);

    // A non-search page must NOT show the toggle (it was a global header widget; now it isn't).
    await page.goto('/queue');
    await expect(page.getByRole('button', { name: 'Show manga' })).toHaveCount(0);

    // The Add Series (search) page DOES show it, next to the source picker.
    await page.goto('/search');
    await expect(page.getByRole('button', { name: 'Show manga' })).toHaveCount(1);
});
