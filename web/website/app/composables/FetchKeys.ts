import type { components } from '#open-fetch-schemas/api';
type ActionsFilterRecord = components['schemas']['ActionsFilterRecord'];

export const FetchKeys = {
    FileLibraries: 'FileLibraries',
    Chapters: { All: 'Chapters' },
    Series: { All: 'Series', Id: (id: string) => `Series/${id}` },
    MangaConnector: { Id: (id: string) => `MangaConnector/${id}`, All: 'MangaConnector' },
    Metadata: { Fetchers: 'Metadata', Links: 'Metadata/Links', Series: (mangaId: string) => `Metadata/Links/${mangaId}` },
    Libraries: { All: 'Libraries', Id: (id: string) => `Libraries/${id}` },
    Settings: { All: 'Settings' },
    Actions: { Types: 'Actions/Types', Page: (filter: ActionsFilterRecord, page: number) => `Actions/${JSON.stringify(filter)}/${page}` },
    Jobs: { All: 'Jobs' },
    NotificationConnectors: { All: 'All' },
};
