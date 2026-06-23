import type { components } from '#open-fetch-schemas/api';
type ActionsFilterRecord = components['schemas']['ActionsFilterRecord'];

export const FetchKeys = {
    FileLibraries: 'FileLibraries',
    Chapters: { Series: (seriesId: string) => `Chapters/${seriesId}` },
    Series: { All: 'Series', Id: (id: string) => `Series/${id}`, Rollup: 'Series/Rollup' },
    MangaConnector: { Id: (id: string) => `MangaConnector/${id}`, All: 'MangaConnector' },
    Metadata: { Fetchers: 'Metadata', Links: 'Metadata/Links', Series: (seriesId: string) => `Metadata/Links/${seriesId}` },
    Libraries: { All: 'Libraries', Id: (id: string) => `Libraries/${id}` },
    Settings: { All: 'Settings', DownloadLanguage: 'Settings/DownloadLanguage', JobRetention: 'Settings/JobRetention' },
    Actions: { Types: 'Actions/Types', Page: (filter: ActionsFilterRecord, page: number) => `Actions/${JSON.stringify(filter)}/${page}` },
    JobQueue: { All: 'JobQueue' },
    Torrents: 'Torrents',
    NotificationConnectors: { All: 'All' },
    Version: 'Version',
    Discover: {
        Rails: 'Discover/Rails',
        RailCatalog: 'Discover/RailCatalog',
        Feed: 'Discover/Feed',
        Genres: 'Discover/Genres',
        Genre: (genre: string) => `Discover/Genre/${genre}`,
    },
};
