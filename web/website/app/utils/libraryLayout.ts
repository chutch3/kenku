import type { components } from '#open-fetch-schemas/api';

export type LibraryLayout = components['schemas']['LibraryLayout'];

/** The on-disk layout choices, shared by the add form and the per-series layout select so a new
 * server-side layout shows up in both. */
export const LAYOUT_OPTIONS: { label: string; value: LibraryLayout }[] = [
    { label: 'Flat — all chapters in one folder', value: 'Flat' },
    { label: 'Volume folders — chapters grouped in Vol N/', value: 'VolumeFolder' },
    { label: 'Volume CBZ — one .cbz per volume', value: 'VolumeCBZ' },
];
