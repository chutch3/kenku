import { test, expect, type Page } from '@playwright/test';

const connectors = [
    { key: 'WeebCentral', name: 'WeebCentral', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList' },
    { key: 'Global', name: 'Global', enabled: true, iconUrl: '', supportedLanguages: ['en'], kind: 'ImageList' },
];

const searchResult = {
    key: 's1',
    name: 'I Am A Hero',
    description: 'Hideo vs. the end of the world.',
    releaseStatus: 'Completed',
    sourceIds: [{ key: 'sid1', mangaConnectorName: 'WeebCentral', foreignKey: 's1', objId: '01ABC', websiteUrl: null, useForDownload: false }],
    fileLibraryId: null,
    originalLanguage: 'en',
    coverUrl: '',
};

/** Stub every /v2 endpoint the search → add journey touches; capture the ChangeLibrary call. */
async function stubApi(page: Page, captured: { changeLibrary?: string }) {
    await page.route('**/v2/SeriesSource', (r) => r.fulfill({ json: connectors }));
    await page.route('**/v2/Series', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/Series/Rollup', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/FileLibrary', (r) => r.fulfill({ json: [{ key: 'lib1', libraryName: 'Manga', basePath: '/data/manga' }] }));
    await page.route('**/v2/Search/WeebCentral/I*', (r) => r.fulfill({ json: [searchResult] }));
    await page.route('**/v2/Search/WeebCentral/Chapters**', (r) =>
        r.fulfill({ json: Array.from({ length: 22 }, (_, i) => ({ chapterNumber: `${i + 1}`, volumeNumber: null, title: null })) }));
    await page.route('**/v2/Series/s1/ChangeLibrary/lib1**', (r) => {
        captured.changeLibrary = r.request().url();
        return r.fulfill({ json: {} });
    });
}

test('search → add modal previews chapters → Add & download flips the card', async ({ page }) => {
    const captured: { changeLibrary?: string } = {};
    await stubApi(page, captured);

    await page.goto('/search');
    await page.getByRole('button', { name: 'WeebCentral' }).click();
    const input = page.getByPlaceholder('Search by title, or paste a series URL…');
    await input.fill('I Am A Hero');
    await input.press('Enter');

    // The result card opens the add modal — not a navigation.
    await page.locator('.kenku-lift').first().click();
    await expect(page).toHaveURL(/\/search/);
    await expect(page.getByText('22 chapters available from WeebCentral')).toBeVisible();

    await page.getByRole('button', { name: 'Add & download' }).click();

    await expect.poll(() => captured.changeLibrary).toContain('download=true');
    await expect(page.getByText('Added I Am A Hero — downloading', { exact: true })).toBeVisible();
});
