import type { components } from '#open-fetch-schemas/api';
type MinimalSeries = components['schemas']['MinimalSeries'];

/** Title equality loose enough for cross-source matching ("Berserk" vs "berserk "). */
export const normalizeTitle = (s?: string | null) => (s ?? '').trim().toLowerCase();

/** Connector-backed series search, shared by the search page and Discover's add-in-place flow. */
export const useSeriesSearch = () => {
    const { $api } = useNuxtApp();

    /** Resolve a series URL through whichever connector owns it. */
    const searchByUrl = async (url: string): Promise<MinimalSeries | null> =>
        (await $api('/v2/Search', { query: { url: JSON.stringify(url) } })) ?? null;

    const searchByConnector = async (connectorName: string, query: string): Promise<MinimalSeries[]> =>
        (await $api('/v2/Search/{MangaConnectorName}/{Query}', {
            path: { MangaConnectorName: connectorName, Query: query },
            method: 'GET',
        })) ?? [];

    return { searchByUrl, searchByConnector };
};
