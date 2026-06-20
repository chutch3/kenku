export type MediaMode = 'manga' | 'comic';

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

    return { mode, setMode };
}
