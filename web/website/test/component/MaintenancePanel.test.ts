import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime';
import { clearNuxtData } from '#imports';
import { flushPromises } from '@vue/test-utils';
import MaintenancePanel from '~/components/MaintenancePanel.vue';

const posted: string[] = [];
let patchedRetention: string | null = null;

for (const task of [
    'CleanupNoDownloadManga',
    'CleanupActions',
    'CleanupOrphanedFiles',
    'ResolveMissingVolumes',
    'SyncChapterFileNames',
    'ResetAndResolveVolumes',
    'PruneCompletedJobs',
]) {
    registerEndpoint(`/v2/Maintenance/${task}`, {
        method: 'POST',
        handler: () => {
            posted.push(task);
            return {};
        },
    });
}
registerEndpoint('/v2/Settings/CompletedJobRetentionDays', () => 3);
registerEndpoint('/v2/Settings/CompletedJobRetentionDays/7', {
    method: 'PATCH',
    handler: () => {
        patchedRetention = '7';
        return {};
    },
});

function findButton(label: string): HTMLButtonElement | undefined {
    return [...document.body.querySelectorAll('button')].find((b) => b.textContent?.includes(label)) as
        | HTMLButtonElement
        | undefined;
}

describe('MaintenancePanel', () => {
    beforeEach(() => {
        posted.length = 0;
        patchedRetention = null;
        document.body.innerHTML = '';
        clearNuxtData();
    });

    it('triggers each maintenance task against its endpoint', async () => {
        await mountSuspended(MaintenancePanel, { attachTo: document.body });

        const buttons: [string, string][] = [
            ['Clean database', 'CleanupNoDownloadManga'],
            ['Clean actions', 'CleanupActions'],
            ['Clean orphaned files', 'CleanupOrphanedFiles'],
            ['Resolve missing volumes', 'ResolveMissingVolumes'],
            ['Sync file names', 'SyncChapterFileNames'],
            ['Prune completed jobs', 'PruneCompletedJobs'],
        ];
        for (const [label, task] of buttons) {
            const button = findButton(label);
            expect(button, `button "${label}"`).toBeTruthy();
            button!.click();
            await flushPromises();
            expect(posted).toContain(task);
        }
    });

    it('saves the completed-job retention window', async () => {
        const wrapper = await mountSuspended(MaintenancePanel, { attachTo: document.body });
        await vi.waitFor(() => expect((wrapper.find('input').element as HTMLInputElement).value).toBe('3'));

        await wrapper.find('input').setValue('7');
        await wrapper.find('input').trigger('change');
        await wrapper.find('input').trigger('blur');
        findButton('Save retention')!.click();

        await vi.waitFor(() => expect(patchedRetention).toBe('7'));
    });
});
