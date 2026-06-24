import { THEMES, getTheme } from '~/theme/registry';

/** The app-wide colour theme. Persisted in a cookie so it survives reloads and is SSR-stable (the
 * theme plugin renders `data-theme` server-side from it, so there's no flash); a shared useState
 * keeps every consumer in sync within a session. Orthogonal to light/dark, which stays on colorMode. */
export function useTheme() {
    const cookie = useCookie<string>('kenku-theme', { default: () => 'karasu', sameSite: 'lax' });
    const current = useState<string>('kenku-theme', () => cookie.value ?? 'karasu');

    const set = (id: string) => {
        if (!getTheme(id)) return; // ignore unknown ids
        current.value = id;
        cookie.value = id;
    };

    return { current, themes: THEMES, set };
}
