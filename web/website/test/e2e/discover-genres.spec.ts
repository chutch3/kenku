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
                json: { apiKey: '', metronConfigured: false, discoveryGenres: state.genres, syncedIndexers: [], downloadClients: [] },
            })),
        page.route('**/v2/Settings/DiscoveryGenres', async (r) => {
            state.genres = r.request().postDataJSON() as string[];
            return r.fulfill({ json: {} });
        }),
        page.route('**/v2/Discover/Manga', (r) => r.fulfill({ json: [entry('Berserk', 1)] })),
        page.route('**/v2/Discover/Manga/Genre/Action', (r) => r.fulfill({ json: [entry('Sakamoto Days', 5)] })),
        page.route('**/v2/Discover/Manga/Genre/Horror', (r) => r.fulfill({ json: [entry('Uzumaki', 6)] })),
    ]);
}

test('editing discovery genres updates the rails on the next Discover visit', async ({ page }) => {
    const state = { genres: ['Action'] };
    await stubApi(page, state);

    await page.goto('/discover');
    await expect(page.getByText('Sakamoto Days')).toBeVisible();

    // Edit genres on the settings page (SPA navigation, warm caches). Committing a tag saves it.
    await page.getByRole('link', { name: 'Settings' }).first().click();
    await page.getByRole('tab', { name: 'Discovery' }).click();
    const tags = page.getByPlaceholder('Add a genre…');
    await tags.click();
    await tags.fill('Horror');
    await tags.press('Enter');
    await expect.poll(() => state.genres).toEqual(['Action', 'Horror']);

    // Back to Discover the way a user would — through the nav, not a reload.
    await page.getByRole('link', { name: 'Discover' }).first().click();
    await expect(page.getByText('Uzumaki')).toBeVisible();
});

test('a genre typed but not committed with Enter is still saved', async ({ page }) => {
    const state = { genres: ['Action'] };
    await stubApi(page, state);

    await page.goto('/settings');
    await page.getByRole('tab', { name: 'Discovery' }).click();
    const tags = page.getByPlaceholder('Add a genre…');
    await tags.click();
    await tags.fill('Horror');
    // No Enter — the user types it and moves on, expecting it to count.
    await tags.press('Tab');

    await expect.poll(() => state.genres).toEqual(['Action', 'Horror']);
});
