import type { components } from '#open-fetch-schemas/api';

type AnySeries = components['schemas']['Series'] | components['schemas']['MinimalSeries'];
type BadgeColor = 'primary' | 'secondary' | 'success' | 'info' | 'warning' | 'error' | 'neutral';

/** How Kenku is handling this series, derived from data already on the list payload. */
export type TrackState = 'untracked' | 'downloading' | 'paused';

export interface StatusMeta {
    label: string;
    color: BadgeColor;
    icon: string;
    hint: string;
}

export function seriesTrackState(series: AnySeries): TrackState {
    if (!series.fileLibraryId) return 'untracked';
    const downloading = (series.sourceIds ?? []).some((s) => s.useForDownload);
    return downloading ? 'downloading' : 'paused';
}

export const TRACK_STATE_META: Record<TrackState, StatusMeta> = {
    untracked: {
        label: 'Not tracked',
        color: 'neutral',
        icon: 'i-lucide-bookmark-plus',
        hint: 'Add this series to a library to start tracking it.',
    },
    downloading: {
        label: 'Downloading',
        color: 'success',
        icon: 'i-lucide-cloud-download',
        hint: 'Kenku is pulling new chapters from your selected sources.',
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

export function trackStateMeta(series: AnySeries): StatusMeta {
    return TRACK_STATE_META[seriesTrackState(series)];
}

export function releaseStatusMeta(series: AnySeries): StatusMeta {
    return RELEASE_STATUS_META[series.releaseStatus] ?? RELEASE_STATUS_META.Unreleased!;
}
