import { test, expect, type Page } from '@playwright/test';

const entry = (title: string, id: number) => ({
    title,
    coverUrl: '',
    url: `https://anilist.co/manga/${id}`,
    source: 'AniList',
    blurb: null,
});

/** Mutable server state: the PATCH updates it exactly like the real settings endpoint. */
function stubApi(page: Page, state: { genres: string[] }) {
    return Promise.all([
        // Catch-all first (Playwright checks the most recent route first).
        page.route('**/v2/**', (r) => r.fulfill({ json: [] })),
        page.route('**/v2/Settings', (r) =>
            r.fulfill({
                json: { apiKey: '', metronConfigured: false, discoveryGenres: state.genres, discoveryFeeds: ['manga'], syncedIndexers: [], downloadClients: [] },
            })),
        page.route('**/v2/Discover/Genres', (r) => r.fulfill({ json: ['Action', 'Horror', 'Romance', 'Sci-Fi', 'Thriller'] })),
        page.route('**/v2/Settings/DiscoveryGenres', async (r) => {
            state.genres = r.request().postDataJSON() as string[];
            return r.fulfill({ json: {} });
        }),
        page.route('**/v2/Discover/Manga', (r) => r.fulfill({ json: [entry('Berserk', 1)] })),
        page.route('**/v2/Discover/Manga/Genre/Action', (r) => r.fulfill({ json: [entry('Sakamoto Days', 5)] })),
        page.route('**/v2/Discover/Manga/Genre/Horror', (r) => r.fulfill({ json: [entry('Uzumaki', 6)] })),
    ]);
}

test('picking a genre updates the rails on the next Discover visit', async ({ page }) => {
    const state = { genres: ['Action'] };
    await stubApi(page, state);

    await page.goto('/discover');
    await expect(page.getByText('Sakamoto Days')).toBeVisible();

    // Edit genres on the settings page (SPA navigation, warm caches). Picking a genre auto-saves.
    await page.getByRole('link', { name: 'Settings' }).first().click();
    await page.getByRole('tab', { name: 'Discovery' }).click();
    await page.getByRole('button', { name: 'Show popup' }).click();
    await page.getByRole('option', { name: 'Horror', exact: true }).click();
    await expect.poll(() => state.genres).toEqual(['Action', 'Horror']);

    // Back to Discover the way a user would — through the nav, not a reload.
    await page.keyboard.press('Escape');
    await page.getByRole('link', { name: 'Discover' }).first().click();
    await expect(page.getByText('Uzumaki')).toBeVisible();
});

test('the genre list offers only AniList genres — no free-text like "Gore"', async ({ page }) => {
    const state = { genres: ['Action'] };
    await stubApi(page, state);

    await page.goto('/settings');
    await page.getByRole('tab', { name: 'Discovery' }).click();
    await page.getByRole('button', { name: 'Show popup' }).click();

    // The valid genres are offered; an unsupported genre simply isn't in the list to pick.
    await expect(page.getByRole('option', { name: 'Horror', exact: true })).toBeVisible();
    await expect(page.getByRole('option', { name: 'Gore', exact: true })).toHaveCount(0);
    expect(state.genres).toEqual(['Action']);
});
