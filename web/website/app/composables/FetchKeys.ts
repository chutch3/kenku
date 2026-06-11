import type { components } from '#open-fetch-schemas/api';
type ActionsFilterRecord = components['schemas']['ActionsFilterRecord'];

export const FetchKeys = {
    FileLibraries: 'FileLibraries',
    Chapters: { Series: (mangaId: string) => `Chapters/${mangaId}` },
    Series: { All: 'Series', Id: (id: string) => `Series/${id}`, Rollup: 'Series/Rollup' },
    MangaConnector: { Id: (id: string) => `MangaConnector/${id}`, All: 'MangaConnector' },
    Metadata: { Fetchers: 'Metadata', Links: 'Metadata/Links', Series: (mangaId: string) => `Metadata/Links/${mangaId}` },
    Libraries: { All: 'Libraries', Id: (id: string) => `Libraries/${id}` },
    Settings: { All: 'Settings', DownloadLanguage: 'Settings/DownloadLanguage', JobRetention: 'Settings/JobRetention' },
    Actions: { Types: 'Actions/Types', Page: (filter: ActionsFilterRecord, page: number) => `Actions/${JSON.stringify(filter)}/${page}` },
    JobQueue: { All: 'JobQueue' },
    NotificationConnectors: { All: 'All' },
    Version: 'Version',
};
