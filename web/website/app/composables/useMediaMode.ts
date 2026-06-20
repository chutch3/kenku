import type { SeriesKind } from './useSeriesKind';

// The mode is exactly a series kind (manga | comic) — reuse the one definition.
export type MediaMode = SeriesKind;

/** The app-wide content axis the user is working in. Directional intent: it filters search/discover to
 * the matching sources and the library view. Persisted in a cookie so it survives reloads (and is
 * SSR-stable); a shared useState keeps every consumer in sync within a session. */
export function useMediaMode() {
    const cookie = useCookie<MediaMode>('media-mode', { default: () => 'manga', sameSite: 'lax' });
    const mode = useState<MediaMode>('media-mode', () => cookie.value ?? 'manga');

    const setMode = (m: MediaMode) => {
        mode.value = m;
        cookie.value = m;
    };

    /** The API ContentType for the active mode — feeds search/discover source filtering. */
    const contentType = computed<'Manga' | 'Comic'>(() => (mode.value === 'comic' ? 'Comic' : 'Manga'));

    return { mode, setMode, contentType };
}
