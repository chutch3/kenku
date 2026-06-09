import { test, expect } from '@playwright/test';

test('a NeedsAttention job shows its series, error and outcome, and can be retried', async ({ page }) => {
    let retried = false;
    await page.route('**/v2/JobQueue', (r) =>
        r.fulfill({
            json: [
                {
                    key: 'j1', type: 'SyncSeriesChapters', status: 'NeedsAttention', attempts: 5, maxAttempts: 5,
                    resourceKey: 's1', error: 'WeebCentral chapter list request was redirected to /404 — the stored series id is wrong.',
                    createdAt: '2026-06-09T10:00:00Z', scheduledFor: '2026-06-09T10:00:00Z',
                    startedAt: '2026-06-09T10:00:01Z', finishedAt: '2026-06-09T10:00:02Z', progress: null,
                },
            ],
        }));
    await page.route('**/v2/Series', (r) =>
        r.fulfill({
            json: [{
                key: 's1', name: 'I Am A Hero', description: '', releaseStatus: 'Completed',
                sourceIds: [], fileLibraryId: 'lib1', originalLanguage: 'en', coverUrl: '',
            }],
        }));
    await page.route('**/v2/Series/Rollup', (r) => r.fulfill({ json: [] }));
    await page.route('**/v2/JobQueue/j1/Retry', (r) => {
        retried = true;
        return r.fulfill({ json: {} });
    });

    await page.goto('/queue');

    await expect(page.getByText('Sync chapters')).toBeVisible();
    await expect(page.getByRole('link', { name: 'I Am A Hero' })).toBeVisible();
    await expect(page.getByText('redirected to /404')).toBeVisible();

    await page.getByRole('button', { name: 'Retry' }).click();
    await expect.poll(() => retried).toBe(true);
});
