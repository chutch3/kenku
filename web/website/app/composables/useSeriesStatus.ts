import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type SeriesRollup = components['schemas']['SeriesRollup'];
type BadgeColor = 'primary' | 'secondary' | 'success' | 'info' | 'warning' | 'error' | 'neutral';

/** How Kenku is handling this series. With a rollup this reflects actual work — pending jobs and
 * missing chapters — not merely whether a source is switched on. */
export type TrackState = 'untracked' | 'attention' | 'downloading' | 'upToDate' | 'paused';

export interface StatusMeta {
    label: string;
    color: BadgeColor;
    icon: string;
    hint: string;
}

export function seriesTrackState(series: AnySeries, rollup?: SeriesRollup | null): TrackState {
    if (!series.fileLibraryId) return 'untracked';
    if (!(series.sourceIds ?? []).some((s) => s.useForDownload)) return 'paused';
    if (!rollup) return 'downloading';
    if (rollup.needsAttentionJobs > 0) return 'attention';
    if (rollup.queuedJobs + rollup.runningJobs > 0 || rollup.downloadedChapters < rollup.wantedChapters) return 'downloading';
    return 'upToDate';
}

export const TRACK_STATE_META: Record<TrackState, StatusMeta> = {
    untracked: {
        label: 'Not tracked',
        color: 'neutral',
        icon: 'i-lucide-bookmark-plus',
        hint: 'Add this series to a library to start tracking it.',
    },
    attention: {
        label: 'Needs attention',
        color: 'error',
        icon: 'i-lucide-triangle-alert',
        hint: 'A job for this series failed and is waiting on you — check the queue.',
    },
    downloading: {
        label: 'Downloading',
        color: 'success',
        icon: 'i-lucide-cloud-download',
        hint: 'Kenku is pulling new chapters from your selected sources.',
    },
    upToDate: {
        label: 'Up to date',
        color: 'secondary',
        icon: 'i-lucide-check',
        hint: 'All wanted chapters are downloaded.',
    },
    paused: { label: 'Paused', color: 'neutral', icon: 'i-lucide-pause', hint: 'Tracked, but no download source is turned on.' },
};

/** Publication status from the connector — distinct from Kenku's tracking state. */
export const RELEASE_STATUS_META: Record<string, StatusMeta> = {
    Continuing: { label: 'Ongoing', color: 'info', icon: 'i-lucide-circle-dot', hint: 'Still being published.' },
    Completed: { label: 'Completed', color: 'success', icon: 'i-lucide-circle-check', hint: 'Finished publishing.' },
    OnHiatus: { label: 'Hiatus', color: 'warning', icon: 'i-lucide-circle-pause', hint: 'Publication is on hold.' },
    Cancelled: { label: 'Cancelled', color: 'error', icon: 'i-lucide-circle-x', hint: 'Publication was cancelled.' },
    Unreleased: { label: 'Unreleased', color: 'neutral', icon: 'i-lucide-circle-dashed', hint: 'Not yet released.' },
};

export function trackStateMeta(series: AnySeries, rollup?: SeriesRollup | null): StatusMeta {
    return TRACK_STATE_META[seriesTrackState(series, rollup)];
}

export function releaseStatusMeta(series: AnySeries): StatusMeta {
    return RELEASE_STATUS_META[series.releaseStatus] ?? RELEASE_STATUS_META.Unreleased!;
}
