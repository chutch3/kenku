import { test, expect, type Page } from '@playwright/test';

// Enough of the settings API to render the page; the theme feature itself is pure client state.
function stubApi(page: Page) {
    return Promise.all([
        page.route('**/v2/**', (r) => r.fulfill({ json: [] })),
        page.route('**/v2/Settings', (r) =>
            r.fulfill({ json: { apiKey: '', metronConfigured: false, discoveryGenres: [], discoveryFeeds: ['manga'], syncedIndexers: [], downloadClients: [] } })
        ),
    ]);
}

test('choosing a theme applies it and persists across a reload', async ({ page }) => {
    await stubApi(page);
    await page.goto('/settings');
    await page.getByRole('tab', { name: 'Appearance' }).click();

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'karasu');

    await page.locator('[data-test="theme-trans-pride"]').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'trans-pride');

    // Cookie-backed + SSR-rendered, so the reloaded document already carries the theme (no flash).
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'trans-pride');
});

test('building a custom theme applies it with a runtime-injected style block', async ({ page }) => {
    await stubApi(page);
    await page.goto('/settings');
    await page.getByRole('tab', { name: 'Appearance' }).click();

    await page.locator('[data-test="apply-custom"]').click();

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'custom');
    await expect(page.locator('#kenku-custom-theme')).toHaveCount(1);
});
